using System;
using System.Diagnostics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Hypostasis.Game.Structures;
using ActionManager = Hypostasis.Game.Structures.ActionManager;
using CSActionManager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace ActionStacksEX.Modules;

/// <summary>
/// Extends the client-side slidecast (safe-to-move) window so movement during the last
/// N seconds of a cast does not interrupt it.
/// </summary>
/// <remarks>
/// Stock FFXIV treats the cast as uncancelable once ActionEffect fills
/// <c>CastInfo.ResponseSpellId</c>. That is the CS-documented slidecast lock — not
/// <c>Interruptible</c>, not <c>OnCastCancelled</c>.
///
/// Live 7.56 (Luis Cure III 01:24): module armed, <c>Interruptible=False</c> at cast
/// start, first <c>SetPosition</c> while remaining≈1.58 / window=2.50, then swallowed
/// <c>OnCastCancelled</c>, then BossMod <c>Casting=False</c> ~0.11s after the move.
/// Ghost AM remaining kept ticking because we swallowed cleanup. The spell did not land.
///
/// This module therefore:
/// <list type="bullet">
/// <item>Copies the current spell into <c>ResponseSpellId</c> / <c>ResponseActionId</c>
/// / <c>ResponseSourceSequence</c> when remaining is inside the slider — the same
/// fields ActionEffect writes when the stock 0.5s window opens.</item>
/// <item>Treats <c>GetCastInfo()-&gt;IsCasting</c> as the only live-cast signal.
/// ActionManager timers after <c>IsCasting</c> is false are ghosts.</item>
/// <item>Logs <c>ActionEffectHandler.Receive</c> for the local player (spell landed)
/// and <c>lost-cast</c> when <c>IsCasting</c> drops without that receive.</item>
/// </list>
/// Dead targets stay cancellable via <c>GameObject.IsDead()</c>. Enable must not
/// read Dalamud <c>ObjectTable</c> (plugin load is off-thread).
/// </remarks>
public unsafe class ExtendedSlidecast : PluginModule
{
    public const float MinWindow = 0f;
    public const float MaxWindow = 2.5f;
    public const float StockWindow = 0.5f;

    /// <summary>OmenTools ExecuteCommandFlag.CancelCast — interrupt the current cast.</summary>
    private const int CancelCastCommand = 105;

    /// <summary>
    /// One-frame lead so the ResponseSpellId lock is applied before remaining
    /// crosses the slider. Not an extra user-facing window.
    /// </summary>
    private const float LockLead = 0.05f;

    private const int LogThrottleMs = 250;

    public override bool ShouldEnable => ActionStacksEX.Config.EnableExtendedSlidecast;

    protected override bool Validate() => OnCastCancelledHook != null
        && CancelCastHook != null
        && Game.fpGetGameObjectFromObjectID != null
        && ActionManager.canUseActionOnGameObject.IsValid;

    protected override void Enable()
    {
        // Plugin load / Hypostasis Toggle runs off the framework thread.
        // Do not touch ObjectTable (or ClientState.LocalPlayer) here.
        latchedActionId = 0;
        localPlayerPtr = 0;
        wasLiveCasting = false;
        sawActionEffect = false;
        lockedSequence = 0;
        EnsureMoveHooks();
        EnsureReceiveHook();

        BattleCharaUpdateHook?.Enable();
        PositionModifiedHook?.Enable();
        SetPositionHook?.Enable();
        UpdateHook?.Enable();
        CancelCastHook.Enable();
        OnCastCancelledHook.Enable();
        ExecuteCommandHook?.Enable();
        ReceiveHook?.Enable(); // optional; missing Receive must not invalidate Enable
        ActionStackManager.PostUseAction += PostUseAction;
        DalamudApi.Framework.Update += FrameworkUpdate;
        DalamudApi.LogInfo($"[ExtendedSlidecast] enabled window={ClampedWindow:F2}s "
            + $"OnCastCancelled={OnCastCancelledHook != null} "
            + $"CancelCast={CancelCastHook != null} "
            + $"ExecuteCommand={ExecuteCommandHook != null} "
            + $"AM.Update={UpdateHook != null} "
            + $"SetPosition={SetPositionHook != null} "
            + $"PositionModified={PositionModifiedHook != null} "
            + $"BattleChara.Update={BattleCharaUpdateHook != null} "
            + $"ActionEffect.Receive={ReceiveHook != null}");
    }

    protected override void Disable()
    {
        DalamudApi.Framework.Update -= FrameworkUpdate;
        ActionStackManager.PostUseAction -= PostUseAction;
        ReceiveHook?.Disable();
        ExecuteCommandHook?.Disable();
        OnCastCancelledHook.Disable();
        CancelCastHook.Disable();
        UpdateHook?.Disable();
        SetPositionHook?.Disable();
        PositionModifiedHook?.Disable();
        BattleCharaUpdateHook?.Disable();
        localPlayerPtr = 0;
        wasLiveCasting = false;
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

    private static Hook<ReceiveDelegate> ReceiveHook;
    private delegate void ReceiveDelegate(uint casterEntityId, CSCharacter* casterPtr, System.Numerics.Vector3* targetPos,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds);

    private static Hook<CSGameObject.Delegates.SetPosition> SetPositionHook;
    private static Hook<BattleChara.Delegates.PositionModified> PositionModifiedHook;
    private static Hook<BattleChara.Delegates.Update> BattleCharaUpdateHook;

    private static uint latchedActionId;
    private static nint localPlayerPtr;
    private static bool wasLiveCasting;
    private static bool sawActionEffect;
    private static uint lockedSequence;
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

    private static void EnsureReceiveHook()
    {
        try
        {
            if (ReceiveHook == null && ActionEffectHandler.MemberFunctionPointers.Receive != null)
            {
                ReceiveHook = DalamudApi.GameInteropProvider.HookFromAddress<ReceiveDelegate>(
                    (nint)ActionEffectHandler.MemberFunctionPointers.Receive, ReceiveDetour);
                DalamudApi.SigScanner.AddHook(ReceiveHook, enable: false);
            }
        }
        catch (Exception e)
        {
            DalamudApi.LogWarning("[ExtendedSlidecast] ActionEffect.Receive hook failed", e);
        }
    }

    private static void SetPositionDetour(CSGameObject* obj, float x, float y, float z)
    {
        if (IsLocalPlayer(obj))
        {
            ApplySlideLock("SetPosition");
            var liveBefore = IsCastInfoCasting();
            SetPositionHook.Original(obj, x, y, z);
            NoteCastingEdge("SetPosition");
            if (liveBefore)
                LogMove("SetPosition");
            return;
        }

        SetPositionHook.Original(obj, x, y, z);
    }

    private static void PositionModifiedDetour(BattleChara* obj)
    {
        if (IsLocalPlayer(obj))
        {
            ApplySlideLock("PositionModified");
            var liveBefore = IsCastInfoCasting();
            PositionModifiedHook.Original(obj);
            NoteCastingEdge("PositionModified");
            if (liveBefore)
                LogMove("PositionModified");
            return;
        }

        PositionModifiedHook.Original(obj);
    }

    private static void BattleCharaUpdateDetour(BattleChara* obj)
    {
        if (IsLocalPlayer(obj))
        {
            CacheLocalPlayer();
            ApplySlideLock("BattleChara.Update");
            BattleCharaUpdateHook.Original(obj);
            NoteCastingEdge("BattleChara.Update");
            return;
        }

        BattleCharaUpdateHook.Original(obj);
    }

    private static void UpdateDetour(CSActionManager* actionManager)
    {
        ApplySlideLock("pre-AM.Update");
        UpdateHook.Original(actionManager);
        ApplySlideLock("post-AM.Update");
        NoteCastingEdge("AM.Update");
    }

    private static void FrameworkUpdate(IFramework framework)
    {
        try
        {
            CacheLocalPlayer();
            ApplySlideLock("Framework.Update");
            NoteCastingEdge("Framework.Update");
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
        sawActionEffect = false;
        ApplySlideLock("PostUseAction");
        NoteCastingEdge("PostUseAction");
    }

    private static void ReceiveDetour(uint casterEntityId, CSCharacter* casterPtr, System.Numerics.Vector3* targetPos,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
    {
        ReceiveHook.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
        if (casterPtr == null || !IsLocalPlayer(casterPtr) || header == null)
            return;

        sawActionEffect = true;
        LogAlways($"ActionEffect action={header->ActionId} spell={header->SpellId} "
            + $"srcSeq={header->SourceSequence} casting={IsCastInfoCasting()} "
            + $"remaining={FormatRemaining()} response={ResponseSpellId()}");
    }

    private static void OnCastCancelledDetour(CSActionManager* actionManager)
    {
        if (ShouldSwallowCancel("OnCastCancelled"))
            return;
        OnCastCancelledHook.Original(actionManager);
        NoteCastingEdge("OnCastCancelled.Original");
    }

    private static void CancelCastDetour(Hotbar* hotbar)
    {
        if (ShouldSwallowCancel("Hotbar.CancelCast"))
            return;
        CancelCastHook.Original(hotbar);
        NoteCastingEdge("Hotbar.CancelCast.Original");
    }

    private static bool ExecuteCommandDetour(int command, int param1, int param2, int param3, int param4)
    {
        if (command == CancelCastCommand && ShouldSwallowCancel("ExecuteCommand(105)"))
            return true;
        return ExecuteCommandHook.Original(command, param1, param2, param3, param4);
    }

    private static bool ShouldSwallowCancel(string source)
    {
        // If IsCasting is already false, the interrupt already happened. Swallowing
        // OnCastCancelled here only preserves ghost AM timers — Luis 01:24.
        if (!IsCastInfoCasting())
        {
            LogAlways($"too-late {source} casting=False action={latchedActionId} "
                + $"sawEffect={sawActionEffect} response={ResponseSpellId()}");
            return false;
        }

        if (IsProtectedWindow())
        {
            LogThrottled($"swallowed {source} action={CurrentActionId()} remaining={FormatRemaining()} "
                + $"window={ClampedWindow:F2} casting=True response={ResponseSpellId()}");
            return true;
        }

        LogThrottled($"allow-cancel {source} action={CurrentActionId()} remaining={FormatRemaining()} window={ClampedWindow:F2} "
            + $"casting=True response={ResponseSpellId()} deadTarget={IsCastTargetDead()}");
        return false;
    }

    /// <summary>
    /// CS: once ResponseSpellId is set, "cast can't be cancelled — this is the start
    /// of the slidecast window". Write those fields when remaining is inside the slider.
    /// </summary>
    private static void ApplySlideLock(string source)
    {
        if (!TryGetLiveCast(out var remaining, out var castInfo, out var actionId))
            return;

        var window = ClampedWindow;
        if (window <= 0f)
            return;
        if (IsCastTargetDead())
            return;
        if (remaining > window + LockLead)
            return;

        latchedActionId = actionId;

        var locked = false;
        if (castInfo != null)
            locked |= LockResponse(castInfo, actionId);

        if (TryGetLocalBattleChara(out var bc))
        {
            var embedded = (CastInfo*)(&bc->CastInfo);
            if (embedded != castInfo && embedded->IsCasting)
                locked |= LockResponse(embedded, actionId);
        }

        if (locked)
        {
            lockedSequence = castInfo != null ? castInfo->SourceSequence : 0;
            LogAlways($"locked ResponseSpellId via {source} action={actionId} remaining={remaining:F2} "
                + $"window={window:F2} spell={ResponseSpellId()} seq={lockedSequence} casting=True");
        }
    }

    private static bool LockResponse(CastInfo* castInfo, uint actionId)
    {
        if (castInfo == null || !castInfo->IsCasting)
            return false;

        if (castInfo->Interruptible)
            castInfo->Interruptible = false;

        if (castInfo->ResponseSpellId != 0)
            return false;

        var spellId = ResolveSpellId(castInfo, actionId);
        castInfo->ResponseSpellId = spellId;
        castInfo->ResponseActionType = (ActionType)castInfo->ActionType;
        castInfo->ResponseActionId = actionId;
        castInfo->ResponseSourceSequence = castInfo->SourceSequence;
        return true;
    }

    private static uint ResolveSpellId(CastInfo* castInfo, uint actionId)
    {
        var am = CSActionManager.Instance();
        if (am != null && am->CastSpellId != 0)
            return am->CastSpellId;
        if (castInfo != null && actionId != 0)
        {
            var fromSheet = CSActionManager.GetSpellIdForAction((ActionType)castInfo->ActionType, actionId);
            if (fromSheet != 0)
                return fromSheet;
        }
        return actionId;
    }

    private static void LogMove(string source)
    {
        if (!TryGetLiveCast(out var remaining, out var castInfo, out var actionId))
            return;
        LogThrottled($"move {source} action={actionId} remaining={remaining:F2} window={ClampedWindow:F2} "
            + $"interruptible={(castInfo != null && castInfo->Interruptible)} "
            + $"casting=True response={ResponseSpellId()} protected={IsProtectedWindow()}");
    }

    private static void NoteCastingEdge(string source)
    {
        var live = IsCastInfoCasting();
        if (wasLiveCasting && !live)
        {
            var outcome = sawActionEffect ? "ended after ActionEffect" : "lost-cast without ActionEffect";
            LogAlways($"{outcome} via {source} action={latchedActionId} remaining={FormatRemaining()} "
                + $"response={ResponseSpellId()} window={ClampedWindow:F2} seq={lockedSequence}");
            sawActionEffect = false;
            lockedSequence = 0;
        }
        else if (!wasLiveCasting && live)
        {
            sawActionEffect = false;
        }

        wasLiveCasting = live;
        if (live && TryGetLiveCast(out _, out _, out var actionId))
            latchedActionId = actionId;
    }

    private static void LogCastState()
    {
        if (!TryGetLiveCast(out var remaining, out var castInfo, out var actionId))
            return;

        var key = IsProtectedWindow() ? "protected" : "casting";
        LogThrottled($"{key} action={actionId} remaining={remaining:F2} window={ClampedWindow:F2} "
            + $"interruptible={(castInfo != null && castInfo->Interruptible)} "
            + $"casting=True response={ResponseSpellId()} sawEffect={sawActionEffect}");
    }

    private static bool IsProtectedWindow()
    {
        if (ClampedWindow <= 0f)
            return false;
        if (IsCastTargetDead())
            return false;
        if (!TryGetLiveCast(out var remaining, out _, out _))
            return false;
        return remaining > 0f && remaining <= ClampedWindow;
    }

    /// <summary>
    /// Live remaining from <c>GetCastInfo()</c> / embedded CastInfo only while
    /// <c>IsCasting</c> is true. ActionManager timers after the bit drops are ghosts.
    /// </summary>
    private static bool TryGetLiveCast(out float remaining, out CastInfo* castInfo, out uint actionId)
    {
        remaining = 0f;
        actionId = 0;
        castInfo = null;

        if (TryGetLocalCastInfo(out castInfo) && castInfo->IsCasting && castInfo->TotalCastTime > 0f)
        {
            remaining = castInfo->TotalCastTime - castInfo->CurrentCastTime;
            actionId = castInfo->ActionId;
            return remaining > 0f;
        }

        if (TryGetLocalBattleChara(out var bc) && bc->CastInfo.IsCasting && bc->CastInfo.TotalCastTime > 0f)
        {
            castInfo = (CastInfo*)(&bc->CastInfo);
            remaining = bc->CastInfo.TotalCastTime - bc->CastInfo.CurrentCastTime;
            actionId = bc->CastInfo.ActionId;
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

    private static uint CurrentActionId()
    {
        if (TryGetLiveCast(out _, out _, out var id))
            return id;
        return latchedActionId;
    }

    private static string FormatRemaining()
    {
        if (TryGetLiveCast(out var remaining, out _, out _))
            return remaining.ToString("F2");
        return "n/a";
    }

    private static void LogAlways(string message)
    {
        lastLogKey = message;
        lastLogMs = logClock.ElapsedMilliseconds;
        DalamudApi.LogDebug($"[ExtendedSlidecast] {message}");
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
