using ActionStacksEX.Modules;
using Hypostasis.Game.Structures;

namespace ActionStacksEX.Modules;

public class CameraRelativeActions : PluginModule
{
    protected override unsafe void Enable()
    {
        ActionStackManager.PostActionStack += PostActionStack;
    }

    protected override unsafe void Disable()
    {
        ActionStackManager.PostActionStack -= PostActionStack;
    }

    private unsafe void PostActionStack(ActionManager* actionManager, uint actionType, uint actionID, uint adjustedActionID, ref ulong targetObjectID, uint param, uint useType, int pvp)
    {
    }
}