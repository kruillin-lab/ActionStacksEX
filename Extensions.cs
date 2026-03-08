using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System.Linq;
using ECommons.GameFunctions;

namespace ActionStacksEX;

public static unsafe class Extensions
{
    public static bool IsJobCategory(this Lumina.Excel.Sheets.Action action, uint jobCategoryId)
    {
        return action.ClassJobCategory.Value.RowId == jobCategoryId;
    }

    public static bool IsPlayerJobCategory(IPlayerCharacter pc, uint jobCategoryId)
    {
        return pc.ClassJob.RowId == jobCategoryId;
    }

    public static bool IsCharacter(GameObject* obj)
    {
        var kind = (int)obj->ObjectKind;
        return kind == 1 || kind == 2; // 1 = Player, 2 = BattleNpc
    }

    public static bool IsHostile(GameObject* obj)
    {
        var kind = (int)obj->ObjectKind;
        var targetAddress = (nint)obj;
        
        // FIRST: Check if this object is in our party - party members are NEVER hostile
        // This handles both Players (kind=1) and Trust NPCs (kind=2, BattleNpc)
        var partyMembers = Common.GetPartyMembers().ToList();
        foreach (var partyMemberAddress in partyMembers)
        {
            if (partyMemberAddress == targetAddress)
            {
                return false;
            }
        }

        if (kind == 1) // Player (not in party)
        {
            var charPtr = (Character*)obj;
            // 0x1A4 = StatusFlags. 0x20 = Hostile (PvP/Duel)
            return (*(uint*)((nint)charPtr + 0x1A4) & 0x20) != 0;
        }
        if (kind == 2) // BattleNpc (not in party)
        {
            var charPtr = (Character*)obj;
            // Use StatusFlags (offset 0x1A4) to determine hostility. 
            // Bit 0x02 is typically the "Hostile" flag (Red nameplate).
            // We can also check 0x20 to align with the Player check if relevant, but 0x02 is standard for NPCs.
            var statusFlags = *(uint*)((nint)charPtr + 0x1A4);
            return (statusFlags & 0x02) != 0; 
        }
        return false;
    }

    public static bool IsEnemy(this IGameObject obj)
    {
        if (obj is IBattleChara bc)
        {
            return IsHostile((GameObject*)bc.Address);
        }
        return false;
    }

    public static bool IsHostile(this IBattleChara bc)
    {
        return IsHostile((GameObject*)bc.Address);
    }

    public static StatusManager* GetStatusManager(GameObject* obj)
    {
        var kind = (int)obj->ObjectKind;
        if (kind != 1 && kind != 2) return null;
        return ((Character*)obj)->GetStatusManager();
    }

    public static float GetHealthRatio(GameObject* obj)
    {
        var kind = (int)obj->ObjectKind;
        if (kind != 1 && kind != 2) return 1.0f;
        var charPtr = (Character*)obj;
        // Raw offsets for current/max health in Dawntrail: 0x1C0 (current), 0x1C8 (max)
        uint hp = *(uint*)((nint)charPtr + 0x1C0);
        uint maxHp = *(uint*)((nint)charPtr + 0x1C8);
        if (maxHp == 0) return 1.0f;
        return (float)hp / maxHp;
    }
}