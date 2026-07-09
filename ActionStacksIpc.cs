using System;
using Dalamud.Plugin.Ipc;

namespace ActionStacksEX;

public static class ActionStacksIpc
{
    public const string PrepareActionName = "ActionStacksEX.PrepareAction";

    private static ICallGateProvider<uint, ulong, (bool Matched, uint ActionID, ulong TargetObjectID, string StackName)>? prepareActionProvider;

    public static void Initialize()
    {
        prepareActionProvider = DalamudApi.PluginInterface.GetIpcProvider<uint, ulong, (bool, uint, ulong, string)>(PrepareActionName);
        prepareActionProvider.RegisterFunc(PrepareAction);
    }

    public static void Dispose()
    {
        prepareActionProvider?.UnregisterFunc();
        prepareActionProvider = null;
    }

    private static (bool Matched, uint ActionID, ulong TargetObjectID, string StackName) PrepareAction(uint actionID, ulong targetObjectID)
    {
        try
        {
            if (ActionStackManager.TryPrepareStackAction(actionID, targetObjectID, out var resolvedAction, out var resolvedTarget, out var stackName))
            {
                DalamudApi.LogDebug($"[ActionStacksEX] IPC prepared '{stackName}': {actionID} -> {resolvedAction} target={resolvedTarget:X}");
                return (true, resolvedAction, resolvedTarget, stackName);
            }
        }
        catch (Exception e)
        {
            DalamudApi.LogError($"[ActionStacksEX] IPC PrepareAction failed for {actionID}\n{e}");
        }

        return (false, actionID, targetObjectID, string.Empty);
    }
}
