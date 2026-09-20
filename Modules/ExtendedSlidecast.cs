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
/// NoClippy (UnknownX7) hooks the same address
/// (<c>48 8B C4 48 83 EC 48 48 89 58 08</c> = CS <c>ActionManager.OnCastCancelled</c>)
/// but does <b>not</b> drop cancels: <c>CastInterruptDetour</c> always runs Original,
/// then sets a plugin-local <c>isCasting=false</c> so animation-lock compensation
/// ignores caster-tax. <c>ZoneClient.SendPacket</c> is counted for RTT, not filtered.
/// Swallowing that function (more than NoClippy does) already failed on Luis's
/// Medica III logs: <c>lost-cast without ActionEffect via AM.Update</c>.
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
            + "NoClippy's CastInterrupt sig is the same OnCastCancelled; it always calls Original "
            + "and only tracks anim-lock state. Stock ~0.5s slidecast is the server commit. "
            + "This module does not spoof CastInfo or swallow cancels. Slider has no combat effect.");
    }

    internal static float ClampedWindow => Math.Clamp(ActionStacksEX.Config.SlidecastWindow, MinWindow, MaxWindow);
}
