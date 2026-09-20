using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
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
/// (see FFXIVClientStructs <c>CastInfo.ResponseSpellId</c> and BossMod ActionManagerEx).
/// ReAction's EnableSlidecastQueuing is queue timing, not this interrupt window.
/// Orbwalker-style "block move until slidecast" is the opposite of this module.
///
/// Patch points (FFXIVClientStructs, not invented offsets):
/// <list type="bullet">
/// <item><c>ActionManager.OnCastCancelled</c> — "called whenever a cast is canceled, generally in response to player move"</item>
/// <item><c>CastInfo.Interruptible</c> — movement checks this bit; clearing it is the same as an uninterruptible cast</item>
/// <item><c>GameMain.ExecuteCommand</c> command 105 — OmenTools <c>ExecuteCommandFlag.CancelCast</c> ("中断咏唱")</item>
/// </list>
/// AutoCastCancel still works: a dead/invalid target is never treated as protected.
/// </remarks>
public unsafe class ExtendedSlidecast : PluginModule
{
    public const float MinWindow = 0f;
    public const float MaxWindow = 2.5f;
    public const float StockWindow = 0.5f;

    /// <summary>OmenTools ExecuteCommandFlag.CancelCast — interrupt the current cast.</summary>
    private const int CancelCastCommand = 105;

    public override bool ShouldEnable => ActionStacksEX.Config.EnableExtendedSlidecast;

    protected override bool Validate() => OnCastCancelledHook != null
        && Game.fpGetGameObjectFromObjectID != null
        && ActionManager.canUseActionOnGameObject.IsValid;

    protected override void Enable()
    {
        OnCastCancelledHook.Enable();
        ExecuteCommandHook?.Enable();
        DalamudApi.Framework.Update += Update;
    }

    protected override void Disable()
    {
        DalamudApi.Framework.Update -= Update;
        ExecuteCommandHook?.Disable();
        OnCastCancelledHook.Disable();
    }

    [HypostasisClientStructsInjection(typeof(CSActionManager.MemberFunctionPointers), Required = true, EnableHook = false)]
    private static Hook<OnCastCancelledDelegate> OnCastCancelledHook;
    private delegate void OnCastCancelledDelegate(CSActionManager* actionManager);

    [HypostasisClientStructsInjection(typeof(GameMain.MemberFunctionPointers), Required = false, EnableHook = false)]
    private static Hook<ExecuteCommandDelegate> ExecuteCommandHook;
    private delegate bool ExecuteCommandDelegate(int command, int param1, int param2, int param3, int param4);

    private static void OnCastCancelledDetour(CSActionManager* actionManager)
    {
        if (IsProtectedWindow())
            return;
        OnCastCancelledHook.Original(actionManager);
    }

    private static bool ExecuteCommandDetour(int command, int param1, int param2, int param3, int param4)
    {
        if (command == CancelCastCommand && IsProtectedWindow())
            return true;
        return ExecuteCommandHook.Original(command, param1, param2, param3, param4);
    }

    private static void Update(IFramework framework)
    {
        if (!IsProtectedWindow())
            return;
        if (!TryGetLocalCastInfo(out var castInfo) || !castInfo->Interruptible)
            return;
        castInfo->Interruptible = false;
    }

    internal static float ClampedWindow => Math.Clamp(ActionStacksEX.Config.SlidecastWindow, MinWindow, MaxWindow);

    private static bool IsProtectedWindow()
    {
        var am = Common.ActionManager;
        if (am == null || am->castActionType == 0)
            return false;

        var window = ClampedWindow;
        if (window <= 0f)
            return false;

        var remaining = am->castTime - am->elapsedCastTime;
        if (remaining <= 0f || remaining > window)
            return false;

        // ActionEffect already arrived: stock slidecast has started; do not swallow
        // non-movement cancels (stun, death, explicit cancel after snapshot).
        if (TryGetLocalCastInfo(out var castInfo) && castInfo->ResponseSpellId != 0)
            return false;

        // AutoCastCancel: dead / invalid targets must still be cancellable.
        if (IsCastTargetInvalid(am))
            return false;

        return true;
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

    private static bool IsCastTargetInvalid(ActionManager* am)
    {
        if (am->castActionType != 1)
            return false;
        if (!ActionStacksEX.actionSheet.TryGetValue(am->castActionID, out var a) || a.TargetArea)
            return false;

        var o = Game.GetGameObjectFromObjectID(am->castTargetObjectID);
        if (o == null)
            return false;

        return !ActionManager.CanUseActionOnGameObject(am->castActionID, o);
    }
}
