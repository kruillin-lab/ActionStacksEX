using System;
using System.Diagnostics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Hypostasis.Game.Structures;
using ActionManager = Hypostasis.Game.Structures.ActionManager;
using CSActionManager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace ActionStacksEX.Modules;

/// <summary>
/// Extends the client-side slidecast (safe-to-move) window so movement during the last
/// N seconds of a cast does not interrupt it.
/// </summary>
/// <remarks>
/// Stock FFXIV locks the cast when ActionEffect arrives (~0.5s remaining;
/// <c>CastInfo.ResponseSpellId</c>). Movement cancel is not
/// <c>ActionManager.OnCastCancelled</c> (cleanup after the fact) and is not
/// <c>EventHandler.CancelByPlayerMovement</c> (quest/event). Live 7.56: slider at
/// 2.5s still interrupted, so the cancel runs on the position-write path.
///
/// CS-documented movement points (no invented offsets):
/// <list type="bullet">
/// <item><c>GameObject.SetPosition</c> — writes new coords</item>
/// <item><c>GameObject.PositionModified</c> (VF55, BattleChara vtable) — fires when
/// the local player's position actually changes</item>
/// <item><c>GameObject.Update</c> (VF36, BattleChara vtable) — native char tick,
/// before Dalamud Framework.Update</item>
/// <item><c>CastInfo.Interruptible</c> on <c>BattleChara.CastInfo</c> /
/// <c>GetCastInfo()</c> (VF80) — same bit uninterruptible casts use</item>
/// <item><c>Hotbar.CancelCast</c> / <c>ExecuteCommand(105)</c> — request path
/// AutoCastCancel already uses; swallowed while protected</item>
/// </list>
/// Dead targets stay cancellable via <c>GameObject.IsDead()</c>. Range/facing
/// failures from <c>CanUseActionOnGameObject</c> are not treated as dead — that
/// check would drop protection the instant you start moving.
///
/// Enable must not read Dalamud <c>ObjectTable.LocalPlayer</c> (plugin load is
/// off-thread; that throws and Hypostasis invalidates the module). Local player
/// comes from CS <c>Control.GetLocalPlayer()</c>, cached on Framework.Update.
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
        // Plugin load / Hypostasis Toggle runs off the framework thread.
        // Do not touch ObjectTable (or ClientState.LocalPlayer) here — that throws
        // "Not on main thread!" and ToggleOrInvalidateModule kills the module
        // before any move hooks are armed. Local player is resolved later from
        // Control.GetLocalPlayer() / Framework.Update.
        latchedProtected = false;
        latchedRemaining = 0f;
        latchedActionId = 0;
        localPlayerPtr = 0;
        EnsureMoveHooks();

        BattleCharaUpdateHook?.Enable();
        PositionModifiedHook?.Enable();
        SetPositionHook?.Enable();
        UpdateHook?.Enable();
        CancelCastHook.Enable();
        OnCastCancelledHook.Enable();
        ExecuteCommandHook?.Enable();
        ActionStackManager.PostUseAction += PostUseAction;
        DalamudApi.Framework.Update += FrameworkUpdate;
        DalamudApi.LogInfo($"[ExtendedSlidecast] enabled window={ClampedWindow:F2}s "
            + $"OnCastCancelled={OnCastCancelledHook != null} "
            + $"CancelCast={CancelCastHook != null} "
            + $"ExecuteCommand={ExecuteCommandHook != null} "
            + $"AM.Update={UpdateHook != null} "
            + $"SetPosition={SetPositionHook != null} "
            + $"PositionModified={PositionModifiedHook != null} "
            + $"BattleChara.Update={BattleCharaUpdateHook != null}");
    }

    protected override void Disable()
    {
        DalamudApi.Framework.Update -= FrameworkUpdate;
        ActionStackManager.PostUseAction -= PostUseAction;
        ExecuteCommandHook?.Disable();
        OnCastCancelledHook.Disable();
        CancelCastHook.Disable();
        UpdateHook?.Disable();
        SetPositionHook?.Disable();
        PositionModifiedHook?.Disable();
        BattleCharaUpdateHook?.Disable();
        latchedProtected = false;
        localPlayerPtr = 0;
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

    private static Hook<CSGameObject.Delegates.SetPosition> SetPositionHook;
    private static Hook<BattleChara.Delegates.PositionModified> PositionModifiedHook;
    private static Hook<BattleChara.Delegates.Update> BattleCharaUpdateHook;

    private static bool latchedProtected;
    private static float latchedRemaining;
    private static uint latchedActionId;
    private static nint localPlayerPtr;
    private static readonly Stopwatch logClock = Stopwatch.StartNew();
    private static long lastLogMs;
    private static string lastLogKey = string.Empty;

    internal static float ClampedWindow => Math.Clamp(ActionStacksEX.Config.SlidecastWindow, MinWindow, MaxWindow);

    private static void EnsureMoveHooks()
    {
        try
        {
            if (SetPositionHook == null && CSGameObject.MemberFunctionPointers.SetPosition != null)
            {
                SetPositionHook = DalamudApi.GameInteropProvider.HookFromAddress<CSGameObject.Delegates.SetPosition>(
                    (nint)CSGameObject.MemberFunctionPointers.SetPosition, SetPositionDetour);
                DalamudApi.SigScanner.AddHook(SetPositionHook, enable: false);
            }
        }
        catch (Exception e)
        {
            DalamudApi.LogWarning("[ExtendedSlidecast] SetPosition hook failed", e);
        }

        var vt = BattleChara.StaticVirtualTablePointer;
        if (vt == null)
            return;

        try
        {
            if (PositionModifiedHook == null)
            {
                PositionModifiedHook = DalamudApi.GameInteropProvider.HookFromAddress<BattleChara.Delegates.PositionModified>(
                    (nint)vt->PositionModified, PositionModifiedDetour);
                DalamudApi.SigScanner.AddHook(PositionModifiedHook, enable: false);
            }
        }
        catch (Exception e)
        {
            DalamudApi.LogWarning("[ExtendedSlidecast] PositionModified hook failed", e);
        }

        try
        {
            if (BattleCharaUpdateHook == null)
            {
                BattleCharaUpdateHook = DalamudApi.GameInteropProvider.HookFromAddress<BattleChara.Delegates.Update>(
                    (nint)vt->Update, BattleCharaUpdateDetour);
                DalamudApi.SigScanner.AddHook(BattleCharaUpdateHook, enable: false);
            }
        }
        catch (Exception e)
        {
            DalamudApi.LogWarning("[ExtendedSlidecast] BattleChara.Update hook failed", e);
        }
    }

    private static void SetPositionDetour(CSGameObject* obj, float x, float y, float z)
    {
        if (IsLocalPlayer(obj))
            OnLocalPlayerMoved("SetPosition");
        SetPositionHook.Original(obj, x, y, z);
    }

    private static void PositionModifiedDetour(BattleChara* obj)
    {
        if (IsLocalPlayer(obj))
            OnLocalPlayerMoved("PositionModified");
        PositionModifiedHook.Original(obj);
    }

    private static void BattleCharaUpdateDetour(BattleChara* obj)
    {
        if (IsLocalPlayer(obj))
        {
            CacheLocalPlayer();
            ApplyInterruptible("BattleChara.Update");
        }
        BattleCharaUpdateHook.Original(obj);
    }

    private static void UpdateDetour(CSActionManager* actionManager)
    {
        ApplyInterruptible("pre-AM.Update");
        UpdateHook.Original(actionManager);
        ApplyInterruptible("post-AM.Update");
        RefreshLatch();
    }

    private static void FrameworkUpdate(IFramework framework)
    {
        try
        {
            CacheLocalPlayer();
            ApplyInterruptible("Framework.Update");
            RefreshLatch();
            LogCastState();
        }
        catch (Exception e)
        {
            DalamudApi.LogError("[ExtendedSlidecast] Framework.Update", e);
        }
    }

    private static void PostUseAction(ActionManager* actionManager, uint actionType, uint actionID, uint adjustedActionID, ulong targetObjectID, uint param, uint useType, int pvp, bool ret)
    {
        if (!ret)
            return;
        CacheLocalPlayer();
        ApplyInterruptible("PostUseAction");
        RefreshLatch();
    }

    private static void OnLocalPlayerMoved(string source)
    {
        ApplyInterruptible(source);
        RefreshLatch();
        if (TryGetRemaining(out var remaining, out var castInfo, out var actionId))
        {
            LogThrottled($"move {source} action={actionId} remaining={remaining:F2} window={ClampedWindow:F2} "
                + $"interruptible={(castInfo != null && castInfo->Interruptible)} "
                + $"protected={IsProtectedWindow(useLatch: false)}");
        }
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
            + $"amType={ActionManagerType()} casting={IsCastInfoCasting()} response={ResponseSpellId()} deadTarget={IsCastTargetDead()}");
        return false;
    }

    private static void ApplyInterruptible(string source)
    {
        if (!TryGetRemaining(out var remaining, out var castInfo, out var actionId))
            return;

        var window = ClampedWindow;
        if (window <= 0f)
            return;
        if (IsCastTargetDead())
            return;
        if (remaining > window + InterruptibleLead)
            return;

        var cleared = false;
        if (castInfo != null && (castInfo->ResponseSpellId == 0) && castInfo->Interruptible)
        {
            castInfo->Interruptible = false;
            cleared = true;
        }

        // BattleChara embeds CastInfo; GetCastInfo() should be the same pointer, but
        // write both if they ever diverge so the move controller cannot miss it.
        if (TryGetLocalBattleChara(out var bc))
        {
            var embedded = (CastInfo*)(&bc->CastInfo);
            if (embedded != castInfo && embedded->IsCasting && embedded->ResponseSpellId == 0 && embedded->Interruptible
                && remaining <= window + InterruptibleLead)
            {
                embedded->Interruptible = false;
                cleared = true;
            }
        }

        if (cleared)
            LogThrottled($"cleared Interruptible via {source} action={actionId} remaining={remaining:F2} window={window:F2}");
    }

    private static void RefreshLatch()
    {
        if (!TryGetRemaining(out var remaining, out var castInfo, out var actionId))
            return;

        var window = ClampedWindow;
        var protectedNow = window > 0f
            && remaining > 0f
            && remaining <= window
            && (castInfo == null || castInfo->ResponseSpellId == 0)
            && !IsCastTargetDead();

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

        if (IsCastTargetDead())
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
    /// Prefer Character.GetCastInfo() remaining, then the embedded BattleChara.CastInfo,
    /// then ActionManager timers.
    /// </summary>
    private static bool TryGetRemaining(out float remaining, out CastInfo* castInfo, out uint actionId)
    {
        remaining = 0f;
        actionId = 0;
        castInfo = null;

        if (TryGetLocalCastInfo(out castInfo) && castInfo->IsCasting && castInfo->TotalCastTime > 0f)
        {
            remaining = castInfo->TotalCastTime - castInfo->CurrentCastTime;
            actionId = castInfo->ActionId;
            if (remaining > 0f)
                return true;
        }

        if (TryGetLocalBattleChara(out var bc) && bc->CastInfo.IsCasting && bc->CastInfo.TotalCastTime > 0f)
        {
            castInfo = (CastInfo*)(&bc->CastInfo);
            remaining = bc->CastInfo.TotalCastTime - bc->CastInfo.CurrentCastTime;
            actionId = bc->CastInfo.ActionId;
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

    private static bool TryGetLocalBattleChara(out BattleChara* battleChara)
    {
        battleChara = ResolveLocalPlayer();
        if (battleChara == null && localPlayerPtr != 0)
            battleChara = (BattleChara*)localPlayerPtr;
        return battleChara != null;
    }

    private static bool TryGetLocalCastInfo(out CastInfo* castInfo)
    {
        castInfo = null;
        if (!TryGetLocalBattleChara(out var battleChara))
            return false;
        castInfo = battleChara->GetCastInfo();
        return castInfo != null;
    }

    /// <summary>
    /// AutoCastCancel still needs dead targets to cancel. Do not use
    /// <c>CanUseActionOnGameObject</c> here — range/facing fail as soon as you move.
    /// </summary>
    private static bool IsCastTargetDead()
    {
        var am = Common.ActionManager;
        if (am == null || am->castActionType != 1)
            return false;
        if (!ActionStacksEX.actionSheet.TryGetValue(am->castActionID, out var a) || a.TargetArea)
            return false;

        var o = Game.GetGameObjectFromObjectID(am->castTargetObjectID);
        if (o == null)
            return false;

        return o->IsDead();
    }

    /// <summary>
    /// CS <c>Control.GetLocalPlayer()</c> — not Dalamud ObjectTable. Safe from native
    /// detours and from Framework.Update. Never throws: login/zoning returns null
    /// and the move hooks stay armed.
    /// </summary>
    private static BattleChara* ResolveLocalPlayer()
    {
        try
        {
            return Control.GetLocalPlayer();
        }
        catch (Exception e)
        {
            DalamudApi.LogWarning("[ExtendedSlidecast] Control.GetLocalPlayer failed", e);
            return null;
        }
    }

    private static void CacheLocalPlayer()
    {
        var player = ResolveLocalPlayer();
        localPlayerPtr = player != null ? (nint)player : 0;
    }

    private static bool IsLocalPlayer(void* obj)
    {
        if (obj == null)
            return false;
        if (localPlayerPtr == 0)
            CacheLocalPlayer();
        return localPlayerPtr != 0 && (nint)obj == localPlayerPtr;
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
