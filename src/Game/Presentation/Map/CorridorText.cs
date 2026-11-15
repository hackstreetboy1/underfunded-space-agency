using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map;

/// <summary>Colour tokens for entry-corridor quality (UI.md 2.1-2.2) and the landing arc's zone verdict; the words are <see cref="MapText.CorridorWord"/> and <see cref="MapText.ZoneVerdictWord"/>.</summary>
public static class CorridorText
{
    /// <summary>A too-steep entry risks the crew (critical); too shallow overshoots (caution).</summary>
    public static string ColorToken(CorridorQuality quality) => quality switch
    {
        CorridorQuality.Ok => "ok",
        CorridorQuality.TooSteep => "critical",
        CorridorQuality.TooShallow => "caution",
        _ => "unknown",
    };

    /// <summary>The landing arc's verdict (X8): <c>ok</c> when the whole arc is in the zone, <c>caution</c> for NEAR EDGE or outside every zone.</summary>
    public static string ZoneVerdictToken(LandingZoneVerdict verdict) => verdict switch
    {
        LandingZoneVerdict.InZone => "ok",
        LandingZoneVerdict.NearEdge or LandingZoneVerdict.OutsideZones => "caution",
        _ => "unknown",
    };
}
