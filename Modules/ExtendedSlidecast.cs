using System;
using System.Diagnostics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Hypostasis.Game.Structures;
using ActionManager = Hypostasis.Game.Structures.ActionManager;
using CSActionManager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager;

namespace ActionStacksEX.Modules;

/// <summary>
/// Extends the client-side slidecast (safe-to-move) window so movement during the last
/// N seconds of a cast does not interrupt it.
/// </summary>
/// <remarks>
/// Stock FFXIV locks the cast when ActionEffect arrives — typically ~0.5s remaining
/// (see FFXIVClientStructs <c>CastInfo.ResponseSpellId</c>).
///
/// Live 7.56 finding (Luis, PR #3 no-op): <c>ActionManager.OnCastCancelled</c> is
/// documented as cleanup ("resets cooldowns and clears temporary state") called
/// <em>in response to</em> a move interrupt. Swallowing it does not stop the interrupt
/// when ActionManager timers are already zeroed (<c>castActionType==0</c>), and
/// <c>Framework.Update</c> clearing <c>CastInfo.Interruptible</c> runs after the native
/// move check. The request path that AutoCastCancel already uses is CS
/// <c>Hotbar.CancelCast</c> (same signature); that wrapper issues
/// <c>GameMain.ExecuteCommand(105)</c> (OmenTools CancelCast).
///
/// Patch points (FFXIVClientStructs / in-repo signatures, not invented offsets):
/// <list type="bullet">
/// <item><c>CastInfo.Interruptible</c> via <c>Character.GetCastInfo()</c> (VF80) — applied
/// inside <c>ActionManager.Update</c> so the move controller sees it this frame</item>
/// <item><c>Hotbar.CancelCast</c> — same function AutoCastCancel calls</item>
/// <item><c>GameMain.ExecuteCommand</c> command 105 — OmenTools CancelCast</item>
/// <item><c>ActionManager.OnCastCancelled</c> — leftover cleanup; swallowed with a latch
/// when AM timers are already cleared</item>
/// </list>
/// AutoCastCancel still works: a dead/invalid target is never treated as protected.
/// EventHandler.CancelByPlayerMovement is quest/event interaction, not combat casts.
/// </remarks>
public unsafe class ExtendedSlidecast : PluginModule
{
    public const float MinWindow = 0f;
    public const float MaxWindow = 2.5f;
    public const float StockWindow = 0.5f;

    /// <summary>OmenTools ExecuteCommandFlag.CancelCast — interrupt the current cast.</summary>
    private const int CancelCastCommand = 105;

    /// <summary>
    /// One-frame lead so Interruptible is cleared before remaining crosses the slider.
    /// Not an extra user-facing window.
    /// </summary>
    private const float InterruptibleLead = 0.05f;

    private const int LogThrottleMs = 250;

    public override bool ShouldEnable => ActionStacksEX.Config.EnableExtendedSlidecast;

    protected override bool Validate() => OnCastCancelledHook != null
        && CancelCastHook != null
        && Game.fpGetGameObjectFromObjectID != null
        && ActionManager.canUseActionOnGameObject.IsValid;

    protected override void Enable()
    {
        latchedProtected = false;
        latchedRemaining = 0f;
        latchedActionId = 0;
        UpdateHook?.Enable();
        CancelCastHook?.Enable();
        OnCastCancelledHook.Enable();
        ExecuteCommandHook?.Enable();
        ActionStackManager.PostUseAction += PostUseAction;
        DalamudApi.Framework.Update += FrameworkUpdate;
        DalamudApi.LogDebug($"[ExtendedSlidecast] enabled window={ClampedWindow:F2}s "
            + $"OnCastCancelled={OnCastCancelledHook != null} "
            + $"CancelCast={CancelCastHook != null} "
            + $"ExecuteCommand={ExecuteCommandHook != null} "
            + $"AM.Update={UpdateHook != null}");
    }

    protected override void Disable()
    {
        DalamudApi.Framework.Update -= FrameworkUpdate;
        ActionStackManager.PostUseAction -= PostUseAction;
        ExecuteCommandHook?.Disable();
        OnCastCancelledHook.Disable();
        CancelCastHook?.Disable();
        UpdateHook?.Disable();
        latchedProtected = false;
    }

    [HypostasisClientStructsInjection(typeof(CSActionManager.MemberFunctionPointers), Required = true, EnableHook = false)]
    private static Hook<OnCastCancelledDelegate> OnCastCancelledHook;
    private delegate void OnCastCancelledDelegate(CSActionManager* actionManager);

    [HypostasisClientStructsInjection(typeof(CSActionManager.MemberFunctionPointers), Required = false, EnableHook = false)]
    private static Hook<ActionManagerUpdateDelegate> UpdateHook;
    private delegate void ActionManagerUpdateDelegate(CSActionManager* actionManager);

    /// <summary>CS <c>Hotbar.CancelCast</c> — same function AutoCastCancel calls.</summary>
    [HypostasisClientStructsInjection(typeof(Hotbar.MemberFunctionPointers), Required = true, EnableHook = false)]
    private static Hook<CancelCastDelegate> CancelCastHook;
    private delegate void CancelCastDelegate(Hotbar* hotbar);

    [HypostasisClientStructsInjection(typeof(GameMain.MemberFunctionPointers), Required = false, EnableHook = false)]
    private static Hook<ExecuteCommandDelegate> ExecuteCommandHook;
    private delegate bool ExecuteCommandDelegate(int command, int param1, int param2, int param3, int param4);

    private static bool latchedProtected;
    private static float latchedRemaining;
    private static uint latchedActionId;
    private static readonly Stopwatch logClock = Stopwatch.StartNew();
    private static long lastLogMs;
    private static string lastLogKey = string.Empty;

    internal static float ClampedWindow => Math.Clamp(ActionStacksEX.Config.SlidecastWindow, MinWindow, MaxWindow);

    private static void UpdateDetour(CSActionManager* actionManager)
    {
        ApplyInterruptible("pre-AM.Update");
        UpdateHook.Original(actionManager);
        ApplyInterruptible("post-AM.Update");
        RefreshLatch();
    }

    private static void FrameworkUpdate(IFramework framework)
    {
        ApplyInterruptible("Framework.Update");
        RefreshLatch();
        LogCastState();
    }

    private static void PostUseAction(ActionManager* actionManager, uint actionType, uint actionID, uint adjustedActionID, ulong targetObjectID, uint param, uint useType, int pvp, bool ret)
    {
        if (!ret)
            return;
        ApplyInterruptible("PostUseAction");
        RefreshLatch();
    }

    private static void OnCastCancelledDetour(CSActionManager* actionManager)
    {
        if (ShouldSwallowCancel("OnCastCancelled"))
            return;
        OnCastCancelledHook.Original(actionManager);
    }

    private static void CancelCastDetour(Hotbar* hotbar)
    {
        if (ShouldSwallowCancel("Hotbar.CancelCast"))
            return;
        CancelCastHook.Original(hotbar);
    }

    private static bool ExecuteCommandDetour(int command, int param1, int param2, int param3, int param4)
    {
        if (command == CancelCastCommand && ShouldSwallowCancel("ExecuteCommand(105)"))
            return true;
        return ExecuteCommandHook.Original(command, param1, param2, param3, param4);
    }

    private static bool ShouldSwallowCancel(string source)
    {
        if (IsProtectedWindow(useLatch: true))
        {
            LogThrottled($"swallowed {source} action={latchedActionId} remaining={FormatRemaining()} window={ClampedWindow:F2}");
            return true;
        }

        LogThrottled($"allow-cancel {source} action={CurrentActionId()} remaining={FormatRemaining()} window={ClampedWindow:F2} "
            + $"amType={ActionManagerType()} casting={IsCastInfoCasting()} response={ResponseSpellId()} invalidTarget={IsCastTargetInvalid()}");
        return false;
    }

    private static void ApplyInterruptible(string source)
    {
        if (!TryGetRemaining(out var remaining, out var castInfo, out var actionId))
            return;
        if (castInfo == null)
            return;

        var window = ClampedWindow;
        if (window <= 0f)
            return;
        if (castInfo->ResponseSpellId != 0)
            return;
        if (IsCastTargetInvalid())
            return;
        if (remaining > window + InterruptibleLead)
            return;
        if (!castInfo->Interruptible)
            return;

        castInfo->Interruptible = false;
        LogThrottled($"cleared Interruptible via {source} action={actionId} remaining={remaining:F2} window={window:F2}");
    }

    private static void RefreshLatch()
    {
        if (!TryGetRemaining(out var remaining, out var castInfo, out var actionId))
        {
            // Keep the last in-window sample so a cancel that already zeroed AM still
            // sees the protected window. Cleared once remaining is no longer live.
            return;
        }

        var window = ClampedWindow;
        var protectedNow = window > 0f
            && remaining > 0f
            && remaining <= window
            && (castInfo == null || castInfo->ResponseSpellId == 0)
            && !IsCastTargetInvalid();

        latchedProtected = protectedNow;
        latchedRemaining = remaining;
        latchedActionId = actionId;
    }

    private static void LogCastState()
    {
        if (!TryGetRemaining(out var remaining, out var castInfo, out var actionId))
            return;

        var window = ClampedWindow;
        var protectedNow = IsProtectedWindow(useLatch: false);
        var key = protectedNow ? "protected" : "casting";
        LogThrottled($"{key} action={actionId} remaining={remaining:F2} window={window:F2} "
            + $"interruptible={(castInfo != null && castInfo->Interruptible)} "
            + $"response={ResponseSpellId()} amType={ActionManagerType()} latched={latchedProtected}");
    }

    private static bool IsProtectedWindow(bool useLatch)
    {
        var window = ClampedWindow;
        if (window <= 0f)
            return false;

        // AutoCastCancel: dead / invalid targets must still be cancellable.
        if (IsCastTargetInvalid())
            return false;

        if (TryGetRemaining(out var remaining, out var castInfo, out _))
        {
            if (castInfo != null && castInfo->ResponseSpellId != 0)
                return false;
            return remaining > 0f && remaining <= window;
        }

        return useLatch && latchedProtected;
    }

    /// <summary>
    /// Prefer Character.GetCastInfo() remaining; fall back to ActionManager timers
    /// when CastInfo is missing or already idle.
    /// </summary>
    private static bool TryGetRemaining(out float remaining, out CastInfo* castInfo, out uint actionId)
    {
        remaining = 0f;
        actionId = 0;
        if (TryGetLocalCastInfo(out castInfo) && castInfo->IsCasting && castInfo->TotalCastTime > 0f)
        {
            remaining = castInfo->TotalCastTime - castInfo->CurrentCastTime;
            actionId = castInfo->ActionId;
            if (remaining > 0f)
                return true;
        }

        var am = Common.ActionManager;
        if (am != null && am->castActionType != 0 && am->castTime > 0f)
        {
            remaining = am->castTime - am->elapsedCastTime;
            actionId = am->castActionID;
            return remaining > 0f;
        }

        return false;
    }

    private static bool TryGetLocalCastInfo(out CastInfo* castInfo)
    {
        castInfo = null;
        var player = DalamudApi.ObjectTable.LocalPlayer;
        if (player == null)
            return false;

        var character = (Character*)player.Address;
        if (character == null)
            return false;

        castInfo = character->GetCastInfo();
        return castInfo != null;
    }

    private static bool IsCastTargetInvalid()
    {
        var am = Common.ActionManager;
        if (am == null || am->castActionType != 1)
            return false;
        if (!ActionStacksEX.actionSheet.TryGetValue(am->castActionID, out var a) || a.TargetArea)
            return false;

        var o = Game.GetGameObjectFromObjectID(am->castTargetObjectID);
        if (o == null)
            return false;

        return !ActionManager.CanUseActionOnGameObject(am->castActionID, o);
    }

    private static bool IsCastInfoCasting() => TryGetLocalCastInfo(out var ci) && ci->IsCasting;

    private static uint ResponseSpellId() => TryGetLocalCastInfo(out var ci) ? ci->ResponseSpellId : 0;

    private static uint ActionManagerType()
    {
        var am = Common.ActionManager;
        return am != null ? am->castActionType : 0;
    }

    private static uint CurrentActionId()
    {
        if (TryGetRemaining(out _, out _, out var id))
            return id;
        return latchedActionId;
    }

    private static string FormatRemaining()
    {
        if (TryGetRemaining(out var remaining, out _, out _))
            return remaining.ToString("F2");
        return latchedProtected ? $"{latchedRemaining:F2}(latch)" : "n/a";
    }

    private static void LogThrottled(string message)
    {
        var now = logClock.ElapsedMilliseconds;
        if (message == lastLogKey && now - lastLogMs < LogThrottleMs)
            return;
        lastLogKey = message;
        lastLogMs = now;
        DalamudApi.LogDebug($"[ExtendedSlidecast] {message}");
    }
}
