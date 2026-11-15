using Usa.Game.Presentation.EntryHud;

namespace Usa.Game.UI.Entry;

/// <summary>Colour tokens (UI.md 2.1) for the entry HUD's tones and knowledge states; status is never colour alone.</summary>
public static class HudToneTokens
{
    public static string Of(HudTone tone) => tone switch
    {
        HudTone.Info => "info",
        HudTone.Ok => "ok",
        HudTone.Caution => "caution",
        HudTone.Critical => "critical",
        HudTone.Unknown => "unknown",
        _ => "text.mid",
    };

    /// <summary>Badge tone for a row's state badge: <c>EST</c> info, <c>STALE</c> caution, <c>UNKNOWN</c> unknown.</summary>
    public static string? BadgeOf(ReadoutState state) => state switch
    {
        ReadoutState.Estimated => "info",
        ReadoutState.Stale => "caution",
        ReadoutState.Unknown => "unknown",
        _ => null,
    };

    /// <summary>Value colour per UI.md 2.3: known and estimated in <c>text.hi</c>, stale in <c>text.mid</c>, unknown as a <c>text.off</c> dash.</summary>
    public static string ValueOf(ReadoutState state) => state switch
    {
        ReadoutState.Stale => "text.mid",
        ReadoutState.Unknown => "text.off",
        _ => "text.hi",
    };

    /// <summary>
    /// The status-language shape for a crew health band (UI.md 2.2), drawn as an approved icon (R3-11). Deceased has no
    /// icon: it is the hollow circle everywhere (glyph map), drawn by <c>StatusGlyph</c>, so it returns null.
    /// </summary>
    public static HudShape? BandShape(CrewHealthBand band) => band switch
    {
        CrewHealthBand.Stable => HudShape.Nominal,
        CrewHealthBand.Stressed or CrewHealthBand.Impaired => HudShape.Caution,
        CrewHealthBand.Critical => HudShape.Failed,
        CrewHealthBand.Deceased => null,
        _ => HudShape.Unknown,
    };
}
