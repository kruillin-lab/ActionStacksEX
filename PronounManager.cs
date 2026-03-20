using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using static ECommons.ExcelServices.ExcelJobHelper;

namespace ActionStacksEX;

public static unsafe class PronounHelpers
{
    public static float GetHPPercent(nint address) => Extensions.GetHealthRatio((GameObject*)address);

    public static uint GetHP(nint address) => ((Character*)address)->CharacterData.Health;

    public static GameObject* GetPartyMemberByStatus(uint status, uint sourceID) => (GameObject*)Common.GetPartyMembers().FirstOrDefault(address => {
        var sm = Extensions.GetStatusManager((GameObject*)address);
        return sm != null && sm->HasStatus(status, sourceID);
    });

    public static GameObject* GetPartyMemberByClassJobID(byte classJob) => (GameObject*)Common.GetPartyMembers().FirstOrDefault(address => ((Character*)address)->CharacterData.ClassJob == classJob);

    public static GameObject* GetPartyMemberByRoleID(byte role) => DalamudApi.DataManager.GetExcelSheet<ClassJob>() is { } sheet
        ? (GameObject*)Common.GetPartyMembers().FirstOrDefault(address => sheet.GetRow(((Character*)address)->CharacterData.ClassJob).Role == role)
        : null;

    public static GameObject* GetPartyMemberByLimitBreak1(uint actionID) => DalamudApi.DataManager.GetExcelSheet<ClassJob>() is { } sheet
        ? (GameObject*)Common.GetPartyMembers().Skip(1).FirstOrDefault(address => sheet.GetRow(((Character*)address)->CharacterData.ClassJob).LimitBreak1.RowId == actionID)
        : null;

    public static unsafe IEnumerable<IBattleChara?> GetPartyMembers()
    {
        foreach (var a in Common.GetPartyMembers())
        {
            yield return (IBattleChara)Svc.Objects.FirstOrDefault(x => x.Address == a);
        }
    }

    public static GameObject* GetMemberByRole(JobRole role)
    {
        nint best = nint.Zero;
        var player = DalamudApi.ClientState.LocalPlayer;

        var members = Common.GetPartyMembers().ToList();
        
        foreach (var member in members)
        {
            
            // Skip self - we want to find OTHER party members with this role
            if (player != null && member == player.Address)
            {
                continue;
            }
                
            var chara = (Character*)member;
            var classJobId = chara->CharacterData.ClassJob;
            
            if (role != JobRole.None)
            {
                var sheet = DalamudApi.DataManager.GetExcelSheet<ClassJob>();
                if (sheet == null) continue;
                var jobRow = sheet.GetRow(classJobId);
                var jobRole = (JobRole)jobRow.Role;
                
                bool match = false;
                if (role == JobRole.DPS)
                {
                    match = jobRole is JobRole.Melee or JobRole.RangedPhysical or JobRole.RangedMagical;
                }
                else
                {
                    match = jobRole == role;
                }
                
                if (!match) 
                {
                    continue;
                }
            }

            return (GameObject*)member;
        }
        
        return (GameObject*)best;
    }

    public static GameObject* GetNearestMember(bool party, bool furthest)
    {
        nint best = nint.Zero;
        float bestDist = furthest ? float.MinValue : float.MaxValue;
        if (DalamudApi.ClientState.LocalPlayer is not { } p) return null;
        Vector3 pPos = p.Position;

        if (party)
        {
            foreach (var member in Common.GetPartyMembers())
            {
                if (member == p.Address) continue;
                float dist = Vector3.Distance(pPos, ((GameObject*)member)->Position);
                if (furthest ? dist > bestDist : dist < bestDist)
                {
                    bestDist = dist;
                    best = member;
                }
            }
        }
        else
        {
            foreach (var obj in Svc.Objects)
            {
                if (obj is IBattleChara bc && Extensions.IsEnemy(bc) && !bc.IsDead)
                {
                    float dist = Vector3.Distance(pPos, bc.Position);
                    if (furthest ? dist > bestDist : dist < bestDist)
                    {
                        bestDist = dist;
                        best = bc.Address;
                    }
                }
            }
        }
        return (GameObject*)best;
    }

    public static GameObject* GetLowestHpMember(JobRole role = JobRole.None)
    {
        nint best = nint.Zero;
        float minHp = float.MaxValue;
        
        var members = Common.GetPartyMembers();
        foreach (var member in members)
        {
            var chara = (Character*)member;
            if (chara->CharacterData.Health == 0) continue; // Skip dead

            if (role != JobRole.None)
            {
                var sheet = DalamudApi.DataManager.GetExcelSheet<ClassJob>();
                if (sheet == null) continue;
                var jobRow = sheet.GetRow(chara->CharacterData.ClassJob);
                var jobRole = (JobRole)jobRow.Role;
                
                bool match = false;
                if (role == JobRole.DPS)
                {
                    match = jobRole is JobRole.Melee or JobRole.RangedPhysical or JobRole.RangedMagical;
                }
                else
                {
                    match = jobRole == role;
                }
                
                if (!match) continue;
            }

            var hp = GetHPPercent(member);
            if (hp < minHp)
            {
                minHp = hp;
                best = member;
            }
        }
        return (GameObject*)best;
    }

    public static bool HasDispellableDebuff(GameObject* obj)
    {
        if (obj == null) return false;
        var kind = (int)obj->ObjectKind;
        if (kind != 1 && kind != 2) return false;
        
        var sm = Extensions.GetStatusManager(obj);
        if (sm == null) return false;
        
        // Status array is fixed at 30 slots - iterate all instead of using NumValidStatuses
        // which may not be reliable
        for (var i = 0; i < 30; i++)
        {
            var status = sm->Status[i];
            if (status.StatusId == 0) continue;

            // Check if status is a dispellable debuff (negative status that can be cleansed)
            var statusSheet = DalamudApi.DataManager.GetExcelSheet<Status>();
            if (statusSheet == null) continue;

            var statusRow = statusSheet.GetRow(status.StatusId);
            if (statusRow.CanDispel && statusRow.StatusCategory == 2) // Category 2 = detrimental
                return true;
        }
        return false;
    }

    public static GameObject* GetDispellablePartyMember()
    {
        nint best = nint.Zero;
        float minHp = float.MaxValue;
        
        var members = Common.GetPartyMembers();
        foreach (var member in members)
        {
            if (!HasDispellableDebuff((GameObject*)member)) continue;
            
            var hp = GetHPPercent(member);
            if (hp < minHp)
            {
                minHp = hp;
                best = member;
            }
        }
        return (GameObject*)best;
    }
}


public interface IGamePronoun
{
    public string Name { get; }
    public string Placeholder { get; }
    public uint ID { get; }
    public unsafe GameObject* GetGameObject();
}

public class HardTargetPronoun : IGamePronoun
{
    public string Name => "Target <t>";
    public string Placeholder => "<t>";
    public uint ID => 10_000;
    public unsafe GameObject* GetGameObject() => (GameObject*)DalamudApi.TargetManager.Target?.Address;
}

public class SelfPronoun : IGamePronoun
{
    public string Name => "Self <me>";
    public string Placeholder => "<me>";
    public uint ID => 10_001;
    public unsafe GameObject* GetGameObject() => (GameObject*)DalamudApi.ClientState.LocalPlayer?.Address;
}

public class FocusTargetPronoun : IGamePronoun
{
    public string Name => "Focus Target <f>";
    public string Placeholder => "<f>";
    public uint ID => 10_002;
    public unsafe GameObject* GetGameObject() => (GameObject*)DalamudApi.TargetManager.FocusTarget?.Address;
}

public class MouseoverPronoun : IGamePronoun
{
    public string Name => "Mouseover <mo>";
    public string Placeholder => "<mo>";
    public uint ID => 10_003;
    public unsafe GameObject* GetGameObject() => (GameObject*)DalamudApi.TargetManager.MouseOverTarget?.Address;
}

public class TargetOfTargetPronoun : IGamePronoun
{
    public string Name => "Target of Target <tt>";
    public string Placeholder => "<tt>";
    public uint ID => 10_004;
    public unsafe GameObject* GetGameObject() => (GameObject*)DalamudApi.TargetManager.Target?.TargetObject?.Address;
}

public class PlayerTargetPronoun : IGamePronoun
{
    public string Name => "Player Target";
    public string Placeholder => "<pt>";
    public uint ID => 10_005;
    public unsafe GameObject* GetGameObject() => DalamudApi.TargetManager.Target is { } t && (int)t.ObjectKind == 1 ? (GameObject*)t.Address : null;
}

public class PartyPronoun : IGamePronoun
{
    public string Name => "Any Party Member";
    public string Placeholder => "<party>";
    public uint ID => 10_010;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetMemberByRole(JobRole.None);
}

public class TankPronoun : IGamePronoun
{
    public string Name => "Tank";
    public string Placeholder => "<tank>";
    public uint ID => 10_012;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetMemberByRole(JobRole.Tank);
}

public class HealerPronoun : IGamePronoun
{
    public string Name => "Healer";
    public string Placeholder => "<healer>";
    public uint ID => 10_013;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetMemberByRole(JobRole.Healer);
}

public class DpsPronoun : IGamePronoun
{
    public string Name => "DPS";
    public string Placeholder => "<dps>";
    public uint ID => 10_014;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetMemberByRole(JobRole.DPS);
}

public class NearestPartyPronoun : IGamePronoun
{
    public string Name => "Nearest (Party)";
    public string Placeholder => "<nearparty>";
    public uint ID => 10_015;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetNearestMember(true, false);
}

public class FurthestPartyPronoun : IGamePronoun
{
    public string Name => "Furthest (Party)";
    public string Placeholder => "<farparty>";
    public uint ID => 10_016;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetNearestMember(true, true);
}

public class NearestEnemyPronoun : IGamePronoun
{
    public string Name => "Nearest (Enemy)";
    public string Placeholder => "<nearemeny>";
    public uint ID => 10_017;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetNearestMember(false, false);
}

public class FurthestEnemyPronoun : IGamePronoun
{
    public string Name => "Furthest (Enemy)";
    public string Placeholder => "<faremeny>";
    public uint ID => 10_018;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetNearestMember(false, true);
}

public class LowestHpPartyPronoun : IGamePronoun
{
    public string Name => "Lowest HP Party Member";
    public string Placeholder => "<lowhp>";
    public uint ID => 10_020;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetLowestHpMember(JobRole.None);
}

public class LowestHpTankPronoun : IGamePronoun
{
    public string Name => "Lowest HP Tank";
    public string Placeholder => "<lowtank>";
    public uint ID => 10_021;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetLowestHpMember(JobRole.Tank);
}

public class LowestHpHealerPronoun : IGamePronoun
{
    public string Name => "Lowest HP Healer";
    public string Placeholder => "<lowhealer>";
    public uint ID => 10_022;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetLowestHpMember(JobRole.Healer);
}

public class LowestHpDpsPronoun : IGamePronoun
{
    public string Name => "Lowest HP DPS";
    public string Placeholder => "<lowdps>";
    public uint ID => 10_023;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetLowestHpMember(JobRole.DPS);
}

public class DeadPronoun : IGamePronoun
{
    public string Name => "Dead Player (in party)";
    public string Placeholder => "<dead>";
    public uint ID => 11_001;

    public unsafe GameObject* GetGameObject() => (GameObject*)(PronounHelpers.GetPartyMembers().FirstOrDefault(x => x != null && x.IsDead && !x.StatusList.Any(y => y.StatusId is 148 or 2648))?.Address);
}

public class OptimalAoEPronoun : IGamePronoun
{
    public string Name => "Optimal AoE Target";
    public string Placeholder => "<optimalaoe>";
    public uint ID => 11_010;
    public unsafe GameObject* GetGameObject()
        => SpatialMath.FindBestAoETarget(0);
}

public class DispellablePartyMemberPronoun : IGamePronoun
{
    public string Name => "Dispellable Party Member";
    public string Placeholder => "<dispel>";
    public uint ID => 11_002;

    public unsafe GameObject* GetGameObject() => PronounHelpers.GetDispellablePartyMember();
}

public class PaladinPronoun : IGamePronoun
{
    private const byte ClassJobID = 19;
    public string Name => "Paladin";
    public string Placeholder => "<pld>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class WarriorPronoun : IGamePronoun
{
    private const byte ClassJobID = 21;
    public string Name => "Warrior";
    public string Placeholder => "<war>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class DarkKnightPronoun : IGamePronoun
{
    private const byte ClassJobID = 32;
    public string Name => "Dark Knight";
    public string Placeholder => "<drk>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class GunbreakerPronoun : IGamePronoun
{
    private const byte ClassJobID = 37;
    public string Name => "Gunbreaker";
    public string Placeholder => "<gnb>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class WhiteMagePronoun : IGamePronoun
{
    private const byte ClassJobID = 24;
    public string Name => "White Mage";
    public string Placeholder => "<whm>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class ScholarPronoun : IGamePronoun
{
    private const byte ClassJobID = 28;
    public string Name => "Scholar";
    public string Placeholder => "<sch>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class AstrologianPronoun : IGamePronoun
{
    private const byte ClassJobID = 33;
    public string Name => "Astrologian";
    public string Placeholder => "<ast>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class SagePronoun : IGamePronoun
{
    private const byte ClassJobID = 40;
    public string Name => "Sage";
    public string Placeholder => "<sge>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class MonkPronoun : IGamePronoun
{
    private const byte ClassJobID = 20;
    public string Name => "Monk";
    public string Placeholder => "<mnk>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class DragoonPronoun : IGamePronoun
{
    private const byte ClassJobID = 22;
    public string Name => "Dragoon";
    public string Placeholder => "<drg>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class NinjaPronoun : IGamePronoun
{
    private const byte ClassJobID = 30;
    public string Name => "Ninja";
    public string Placeholder => "<nin>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class SamuraiPronoun : IGamePronoun
{
    private const byte ClassJobID = 34;
    public string Name => "Samurai";
    public string Placeholder => "<sam>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class ReaperPronoun : IGamePronoun
{
    private const byte ClassJobID = 39;
    public string Name => "Reaper";
    public string Placeholder => "<rpr>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class ViperPronoun : IGamePronoun
{
    private const byte ClassJobID = 41;
    public string Name => "Viper";
    public string Placeholder => "<vpr>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class BardPronoun : IGamePronoun
{
    private const byte ClassJobID = 23;
    public string Name => "Bard";
    public string Placeholder => "<brd>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class MachinistPronoun : IGamePronoun
{
    private const byte ClassJobID = 31;
    public string Name => "Machinist";
    public string Placeholder => "<mch>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class DancerPronoun : IGamePronoun
{
    private const byte ClassJobID = 38;
    public string Name => "Dancer";
    public string Placeholder => "<dnc>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class BlackMagePronoun : IGamePronoun
{
    private const byte ClassJobID = 25;
    public string Name => "Black Mage";
    public string Placeholder => "<blm>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class SummonerPronoun : IGamePronoun
{
    private const byte ClassJobID = 27;
    public string Name => "Summoner";
    public string Placeholder => "<smn>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class RedMagePronoun : IGamePronoun
{
    private const byte ClassJobID = 35;
    public string Name => "Red Mage";
    public string Placeholder => "<rdm>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class PictomancerPronoun : IGamePronoun
{
    private const byte ClassJobID = 42;
    public string Name => "Pictomancer";
    public string Placeholder => "<pct>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public class BlueMagePronoun : IGamePronoun
{
    private const byte ClassJobID = 36;
    public string Name => "Blue Mage";
    public string Placeholder => "<blu>";
    public uint ID => 10_220 + ClassJobID;
    public unsafe GameObject* GetGameObject() => PronounHelpers.GetPartyMemberByClassJobID(ClassJobID);
}

public static class PronounManager
{
    public const int MinimumCustomPronounID = 10_000;

    public static Dictionary<uint, IGamePronoun> CustomPronouns { get; set; } = [];
    public static Dictionary<string, IGamePronoun> CustomPlaceholders { get; set; } = [];
    public static List<uint> OrderedIDs { get; set; } =
    [
        10_000, // Target
        10_001, // Self
        10_002, // Focus Target
        10_003, // Mouseover
        10_004, // TT
        10_005, // Player Target
        10_010, // Party
        10_012, // Tank
        10_013, // Healer
        10_014, // DPS
        10_015, // Nearest (Party)
        10_016, // Furthest (Party)

        10_017, // Nearest (Enemy)
        10_018, // Furthest (Enemy)
        10_020, // Lowest HP Party
        10_021, // Lowest HP Tank
        10_022, // Lowest HP Healer
        10_023, // Lowest HP DPS
        11_001, // Dead
        11_002, // Dispellable Party Member
        11_010, // Optimal AoE
    ];
    public static void Initialize()
    {
        foreach (var t in Util.Assembly.GetTypes<IGamePronoun>())
        {
            if (t.IsInterface || t.IsAbstract) continue;
            var pronoun = (IGamePronoun)Activator.CreateInstance(t);
            if (pronoun == null) continue;

            if (pronoun.ID < MinimumCustomPronounID)
                throw new ApplicationException("Custom pronoun IDs must be above 10000");

            if (!CustomPronouns.ContainsKey(pronoun.ID))
            {
                CustomPronouns.Add(pronoun.ID, pronoun);
                CustomPlaceholders.TryAdd(pronoun.Placeholder, pronoun);
                if (!OrderedIDs.Contains(pronoun.ID))
                    OrderedIDs.Add(pronoun.ID);
            }
        }
    }

    public static string GetPronounName(uint id) => id >= MinimumCustomPronounID && CustomPronouns.TryGetValue(id, out var pronoun)
        ? pronoun.Name
        : ((PronounID)id).ToString();

    public static unsafe GameObject* GetGameObjectFromID(uint id) => PluginModuleManager.GetModule<Modules.ActionStacks>().IsValid ?
            id >= MinimumCustomPronounID && CustomPronouns.TryGetValue(id, out var pronoun)
                ? pronoun.GetGameObject()
                : Common.GetGameObjectFromPronounID((PronounID)id)
            : null;
}