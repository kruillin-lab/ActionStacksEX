using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Hypostasis.Game.Structures;

namespace ActionStacksEX.Modules;

public unsafe class TurboHotbars : PluginModule
{
    private class TurboInfo
    {
        public Stopwatch LastPress { get; } = new();
        public bool LastFramePressed { get; set; } = false;
        public bool LastFrameHeld { get; set; } = false;
        public int RepeatDelay { get; set; } = 0;

        public bool Toggled { get; set; } = false;
        public bool CanToggle { get; set; } = false;
        public Stopwatch TimeHeld { get; set; } = new();

        public bool IsReady => LastPress.IsRunning && LastPress.ElapsedMilliseconds >= RepeatDelay;
    }

    private static readonly Dictionary<uint, TurboInfo> inputIDInfos = new();
    private static bool isAnyTurboRunning;

    public override bool ShouldEnable => ActionStacksEX.Config.EnableTurboHotbars;

    protected override bool Validate() => InputData.isInputIDPressed.IsValid && InputData.isInputIDHeld.IsValid;

    protected override void Enable()
    {
        if (!InputData.isInputIDPressed.IsHooked)
            InputData.isInputIDPressed.CreateHook(IsInputIDPressedDetour, false);
        CheckHotbarBindingsHook.Enable();
        //CheckCrossbarBindingsHook.Enable();
    }

    protected override void Disable()
    {
        InputData.isInputIDPressed.Hook.Disable();
        CheckHotbarBindingsHook.Disable();
        //CheckCrossbarBindingsHook.Disable();
    }

    private static Bool IsInputIDPressedDetour(InputData* inputData, uint id)
    {
        if (!inputIDInfos.TryGetValue(id, out var info))
            inputIDInfos[id] = info = new TurboInfo();

        var isPressed = InputData.isInputIDPressed.Original(inputData, id);
        var isHeld = inputData->IsInputIDHeld(id);
        if (ActionStacksEX.Config.ToggleTurboMode)
        {
            if (isHeld && !info.TimeHeld.IsRunning)
            {
                info.TimeHeld.Restart();
                info.CanToggle = true;
            }
            else if (!isHeld)
            {
                info.TimeHeld.Reset();
            }

            if (info.CanToggle && info.TimeHeld.Elapsed.TotalMilliseconds > 300)
            {
                info.CanToggle = false;
                info.Toggled = !info.Toggled;
                if (info.Toggled)
                {
                    foreach (var i in inputIDInfos.Where(x => x.Key != id).Select(x => x.Value))
                        i.Toggled = false;
                }
                DalamudApi.ChatGui.Print($"Input Key {id} toggled: {info.Toggled}");
            }
        }

        var useHeld = info.IsReady && (ActionStacksEX.Config.EnableTurboHotbarsOutOfCombat || DalamudApi.Condition[ConditionFlag.InCombat]);
        var useToggle = info.Toggled && useHeld && ActionStacksEX.Config.ToggleTurboMode;
        var ret = useToggle ? true : useHeld ? isHeld : (bool)isPressed;
        
        if (ret)
        {
            info.RepeatDelay = isPressed && ActionStacksEX.Config.InitialTurboHotbarInterval > 0 ? ActionStacksEX.Config.InitialTurboHotbarInterval : ActionStacksEX.Config.TurboHotbarInterval;
            info.LastPress.Restart();
        }
        else if (isHeld != info.LastFrameHeld || useToggle)
        {
            if ((isHeld && isAnyTurboRunning) || useToggle)
            {
                info.RepeatDelay = 200;
                info.LastPress.Restart();
            }
            else
            {
                if (!info.Toggled)
                info.LastPress.Reset();
            }
        }

        info.LastFrameHeld = isHeld;
        info.LastFramePressed = isPressed;

        return ret;
    }

    private delegate void CheckHotbarBindingsDelegate(nint a1, byte a2);
    [HypostasisSignatureInjection("89 54 24 10 53 41 55 41 57", Required = true, EnableHook = false)]
    private static Hook<CheckHotbarBindingsDelegate> CheckHotbarBindingsHook;
    private static void CheckHotbarBindingsDetour(nint a1, byte a2)
    {
        isAnyTurboRunning = inputIDInfos.Any(t => t.Value.LastPress.IsRunning);
        InputData.isInputIDPressed.Hook.Enable();
        CheckHotbarBindingsHook.Original(a1, a2);
        InputData.isInputIDPressed.Hook.Disable();
    }

    /*private delegate void CheckCrossbarBindingsDelegate(nint a1, uint a2);
    [HypostasisSignatureInjection("E8 ?? ?? ?? ?? EB 20 E8 ?? ?? ?? ?? 84 C0", Required = true, EnableHook = false)]
    private static Hook<CheckCrossbarBindingsDelegate> CheckCrossbarBindingsHook;
    private static void CheckCrossbarBindingsDetour(nint a1, uint a2)
    {
        isAnyTurboRunning = inputIDInfos.Any(t => t.Value.LastPress.IsRunning);
        // Needs different input functions
        CheckCrossbarBindingsHook.Original(a1, a2);
    }*/
}
