using ActionStacksEX.Modules;
using ActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;
using ActionManager = Hypostasis.Game.Structures.ActionManager;
using Dalamud.Game.ClientState.Conditions;

namespace ActionStacksEX.Modules;

public class AutoDismount : PluginModule
{
    protected override unsafe void Enable()
    {
        ActionStackManager.PreActionStack += PreActionStack;
    }

    protected override unsafe void Disable()
    {
        ActionStackManager.PreActionStack -= PreActionStack;
    }

    private unsafe void PreActionStack(ActionManager* actionManager, ref uint actionType, ref uint actionID, ref uint adjustedActionID, ref ulong targetObjectID, ref uint param, uint useType, ref int pvp, out bool? ret)
    {
        ret = null;
        if (!ActionStacksEX.Config.EnableAutoDismount || actionType != 1 || useType != 1 || DalamudApi.ClientState.LocalPlayer == null) return;

        // Use Condition check for Mounting as it's the most reliable in Dalamud
        if (!DalamudApi.Condition[ConditionFlag.Mounted]) return;

        if (!ActionStacksEX.actionSheet.TryGetValue(adjustedActionID, out var a)) return;

        if (a.ActionCategory.RowId is 1 or 2 or 3 or 4)
        {
            Dismount(actionManager);
        }
    }

    private unsafe void Dismount(ActionManager* actionManager)
    {
        actionManager->CS.UseAction(ActionType.Action, 23);
    }
}