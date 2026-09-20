using System;

namespace ActionStacksEX.Modules;

/// <summary>
/// Intended to extend the client-side slidecast window. Live 7.56 testing showed
/// that window cannot be extended with CS-documented client state.
/// </summary>
/// <remarks>
/// Stock FFXIV lets you move in the last ~0.5s because the server has already
/// committed the action and sent ActionEffect. CS documents that as filling
/// <c>CastInfo.ResponseSpellId</c> — a <b>consequence</b> of the packet, not a
/// client-writable lock.
///
/// Luis Rivera live 7.56 (PR #3):
/// <list type="bullet">
/// <item><c>Interruptible=false</c> at cast start — still cancelled on move.</item>
/// <item>Spoofed <c>ResponseSpellId</c> for the whole cast (remaining &lt; 2.5s
/// window) — still cancelled on move. <c>seq=0</c> on lost-cast.</item>
/// <item><c>Hotbar.CancelCast</c> / <c>ExecuteCommand(105)</c> never fired.
/// <c>OnCastCancelled</c> was swallowed while <c>IsCasting</c> was still true;
/// <c>ActionManager.Update</c> then observed <c>IsCasting</c> drop.</item>
/// <item>Never an <c>ActionEffectHandler.Receive</c> line — the spell did not land.</item>
/// </list>
/// CS has no documented function that gates movement interrupt once ResponseSpellId
/// is already set. The inbound path is <c>PacketDispatcher.HandleActorControlPacket</c>
/// / ActionEffect; blocking those would only hide a server cancel. Outbound cancel
/// opcodes are not CS-documented. Orbwalker-style movement lock and ReAction
/// slidecast queueing are out of scope for this request.
///
/// This module therefore does not hook, spoof CastInfo, or swallow cancels.
/// Enable logs the blocked conclusion once.
/// </remarks>
public class ExtendedSlidecast : PluginModule
{
    public const float MinWindow = 0f;
    public const float MaxWindow = 2.5f;
    public const float StockWindow = 0.5f;

    public override bool ShouldEnable => ActionStacksEX.Config.EnableExtendedSlidecast;

    protected override void Enable()
    {
        DalamudApi.LogInfo("[ExtendedSlidecast] blocked on live 7.56: movement before ActionEffect "
            + "cancels the cast even with ResponseSpellId spoofed and OnCastCancelled swallowed. "
            + "IsCasting drops inside ActionManager.Update; no ActionEffect arrives. "
            + "Stock ~0.5s slidecast is the server commit. This module does not spoof CastInfo "
            + "or swallow cancels. Hotbar.CancelCast / ExecuteCommand(105) are the UI/AutoCastCancel "
            + "path only. Slider has no combat effect.");
    }

    internal static float ClampedWindow => Math.Clamp(ActionStacksEX.Config.SlidecastWindow, MinWindow, MaxWindow);
}
