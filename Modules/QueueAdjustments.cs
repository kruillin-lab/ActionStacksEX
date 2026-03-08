using ActionStacksEX.Modules;
using Hypostasis.Game.Structures;

namespace ActionStacksEX.Modules;

public class QueueAdjustments : PluginModule
{
    protected override unsafe void Enable()
    {
        ActionStackManager.PostActionStack += PostActionStack;
        ActionStackManager.PostUseAction += PostUseAction;
    }

    protected override unsafe void Disable()
    {
        ActionStackManager.PostActionStack -= PostActionStack;
        ActionStackManager.PostUseAction -= PostUseAction;
    }

    private unsafe void PostActionStack(ActionManager* actionManager, uint actionType, uint actionID, uint adjustedActionID, ref ulong targetObjectID, uint param, uint useType, int pvp)
    {
    }

    private unsafe void PostUseAction(ActionManager* actionManager, uint actionType, uint actionID, uint adjustedActionID, ulong targetObjectID, uint param, uint useType, int pvp, bool ret)
    {
    }
}