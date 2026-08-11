using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace ActionStacksEX;

/// <summary>
/// A user-composable targeting pronoun: candidate pool -> filters -> sort -> first match.
/// Defined at runtime in the config UI, usable in action stacks and as a macro placeholder.
/// </summary>
public class ForgedPronounDef
{
    public uint ID = 0;                 // Assigned automatically, >= PronounForge.MinimumForgedID
    public string Name = "New Forged Pronoun";
    public string Placeholder = string.Empty;
    public int Pool = 0;                // 0 = Party, 1 = Enemies
    public int Role = 0;                // 0 = Any, 1 = Tank, 2 = Healer, 3 = DPS
    public uint JobID = 0;              // ClassJob row ID, 0 = any
    public bool ExcludeSelf = false;
    public int LifeFilter = 0;          // 0 = Alive only, 1 = Dead only, 2 = Any
    public bool UseHpFilter = false;
    public float MaxHpPercent = 0.8f;   // Match only if HP% <= this
    public uint StatusID = 0;           // 0 = ignore
    public bool MissingStatus = false;  // If true, match only when status is absent
    public float MaxDistance = 0f;      // 0 = any distance
    public int Sort = 0;                // 0 = First found, 1 = HP% asc, 2 = HP% desc, 3 = Distance asc, 4 = Distance desc
}

public class ForgedPronoun(ForgedPronounDef def) : IGamePronoun
{
    public ForgedPronounDef Def { get; } = def;
    public string Name => Def.Name;
    public string Placeholder => Def.Placeholder;
    public uint ID => Def.ID;
    public unsafe GameObject* GetGameObject() => PronounForge.Resolve(Def);
}

public static unsafe class PronounForge
{
    public const uint MinimumForgedID = 30_000;

    public static GameObject* Resolve(ForgedPronounDef def)
    {
        if (DalamudApi.ObjectTable.LocalPlayer is not { } player) return null;
        var pPos = player.Position;
        var sheet = DalamudApi.DataManager.GetExcelSheet<ClassJob>();

        List<nint> candidates = [];
        if (def.Pool == 0)
        {
            foreach (var a in Common.GetPartyMembers())
                candidates.Add(a);
        }
        else
        {
            foreach (var obj in Svc.Objects)
            {
                if (obj is IBattleChara bc && bc.IsEnemy())
                    candidates.Add(bc.Address);
            }
        }

        nint best = 0;
        var bestKey = float.MaxValue;
        foreach (var addr in candidates)
        {
            if (addr == 0) continue;
            var obj = (GameObject*)addr;
            var chara = (Character*)addr;

            if (def.ExcludeSelf && addr == player.Address) continue;

            var dead = chara->CharacterData.Health == 0;
            if (def.LifeFilter == 0 && dead) continue;
            if (def.LifeFilter == 1 && !dead) continue;

            if (def.Pool == 0)
            {
                if (def.JobID != 0 && chara->CharacterData.ClassJob != def.JobID) continue;

                if (def.Role != 0 && sheet != null)
                {
                    var jobRole = (JobRole)sheet.GetRow(chara->CharacterData.ClassJob).Role;
                    var match = def.Role switch
                    {
                        1 => jobRole == JobRole.Tank,
                        2 => jobRole == JobRole.Healer,
                        3 => jobRole is JobRole.Melee or JobRole.RangedPhysical or JobRole.RangedMagical,
                        _ => true
                    };
                    if (!match) continue;
                }
            }

            var hp = PronounHelpers.GetHPPercent(addr);
            if (def.UseHpFilter && hp > def.MaxHpPercent) continue;

            if (def.StatusID != 0)
            {
                var has = HasStatus(obj, def.StatusID);
                if (def.MissingStatus ? has : !has) continue;
            }

            var dist = Vector3.Distance(pPos, obj->Position);
            if (def.MaxDistance > 0 && dist > def.MaxDistance) continue;

            if (def.Sort == 0) return obj;

            var key = def.Sort switch
            {
                1 => hp,
                2 => -hp,
                3 => dist,
                4 => -dist,
                _ => 0
            };

            if (best == 0 || key < bestKey)
            {
                best = addr;
                bestKey = key;
            }
        }

        return (GameObject*)best;
    }

    private static bool HasStatus(GameObject* obj, uint statusID)
    {
        var sm = Extensions.GetStatusManager(obj);
        if (sm == null) return false;

        for (var i = 0; i < 30; i++)
        {
            if (sm->Status[i].StatusId == statusID)
                return true;
        }
        return false;
    }
}
