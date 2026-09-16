using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Hypostasis.Game.Structures;
using InputId = FFXIVClientStructs.FFXIV.Client.System.Input.InputId;

namespace ActionStacksEX.Modules;

public unsafe class TurboHotbars : PluginModule
{
    /// <summary>
    /// Maps the uint IDs <see cref="IsInputIDPressedDetour"/> already keys on to visible
    /// hotbar slots. Values are <see cref="InputId"/> HOTBAR_1_1..HOTBAR_10_B then
    /// HOTBAR_EX_1..HOTBAR_EX_B (12 slots per bar, slot 10 stored as *_0).
    /// </summary>
    public static class HotbarInputIds
    {
        public const int StandardBars = 10;
        public const int ExtraBar = StandardBars + 1;
        public const int SlotsPerBar = 12;

        public static uint FromSlot(int hotbar, int slot)
            => (uint)((int)InputId.HOTBAR_1_1 + (hotbar - 1) * SlotsPerBar + (slot - 1));

        public static bool TryGetSlot(uint id, out int hotbar, out int slot)
        {
            var first = (uint)InputId.HOTBAR_1_1;
            var last = (uint)InputId.HOTBAR_EX_B;
            if (id < first || id > last)
            {
                hotbar = 0;
                slot = 0;
                return false;
            }

            var offset = id - first;
            hotbar = (int)(offset / SlotsPerBar) + 1;
            slot = (int)(offset % SlotsPerBar) + 1;
            return true;
        }

        public static string Format(uint id)
        {
            if (!TryGetSlot(id, out var hotbar, out var slot))
                return $"Hotbar Input {id}";
            return hotbar == ExtraBar ? $"Extra Hotbar Slot {slot}" : $"Hotbar {hotbar} Slot {slot}";
        }
    }

    private class TurboInfo
    {
        public Stopwatch LastPress { get; } = new();
        public bool LastFramePressed { get; set; } = false;
        public bool LastFrameHeld { get; set; } = false;
        public int RepeatDelay { get; set; } = 0;

        public bool Toggled { get; set; } = false;
        public bool CanToggle { get; set; } = false;
        public Stopwatch TimeHeld { get; set; } = new();

        /// <summary>
        /// QPC timestamp of the last dispatch (0 = none yet). In paced mode this — not
        /// the wall-clock <see cref="LastPress"/> — is the interval anchor: eligibility
        /// is recomputed from it against the live animation lock on every read, so a
        /// stale armed slot can never gate a dispatch.
        /// </summary>
        public long LastPressTimestamp { get; set; } = 0;

        public bool IsReady
        {
            get
            {
                if (ActionStacksEX.Config.EnableTurboPacing)
                {
                    if (LastPressTimestamp == 0) return false;
                    var am = Common.ActionManager;
                    return HardwarePacer.TurboDispatchReady(
                        LastPressTimestamp,
                        RepeatDelay / 1000.0,
                        am != null ? am->animationLock : 0.0,
                        HardwarePacer.QueryTimestamp(),
                        out _);
                }
                return LastPress.IsRunning && LastPress.ElapsedMilliseconds >= RepeatDelay;
            }
        }
    }

    private static readonly Dictionary<uint, TurboInfo> inputIDInfos = new();
    private static bool isAnyTurboRunning;

    /// <summary>Last hotbar input ID that reported a press while the binding check hook ran.</summary>
    public static uint LastPressedHotbarInputId { get; private set; }

    public override bool ShouldEnable => ActionStacksEX.Config.EnableTurboHotbars;

    public static bool IsTurboEligible(uint id)
    {
        var cfg = ActionStacksEX.Config;
        if (!cfg.EnableTurboHotbarFilter)
            return true;

        var ids = cfg.TurboHotbarInputIds;
        return ids == null || ids.Count == 0 || ids.Contains(id);
    }

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
        var isPressed = InputData.isInputIDPressed.Original(inputData, id);
        if (isPressed)
            LastPressedHotbarInputId = id;

        if (!IsTurboEligible(id))
            return isPressed;

        if (!inputIDInfos.TryGetValue(id, out var info))
            inputIDInfos[id] = info = new TurboInfo();

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
            // Paced-mode dispatch anchor: the engine re-derives the slot from this raw
            // timestamp against the live animation lock on every IsReady read.
            info.LastPressTimestamp = HardwarePacer.QueryTimestamp();
        }
        else if (isHeld != info.LastFrameHeld || useToggle)
        {
            if ((isHeld && isAnyTurboRunning) || useToggle)
            {
                info.RepeatDelay = 200;
                info.LastPress.Restart();
                info.LastPressTimestamp = HardwarePacer.QueryTimestamp();
            }
            else
            {
                if (!info.Toggled)
                {
                    info.LastPress.Reset();
                    info.LastPressTimestamp = 0; // paced mode: never-pressed gates IsReady
                }
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
