namespace Usa.Game.UI.Map;

/// <summary>
/// The parts of the X8 ground-tracking picture <see cref="Presentation.Map.OrbitMapInput"/> does not carry yet: the
/// marker's along-track tick and the STALE state of the orbit chip. Station ticks and the <c>Next fix in 9 min</c> chip come
/// from the frame (<c>OrbitMapInput.Stations</c>, <c>NextFixAt</c>). The host fills this from <c>OrbitMapInputAdapter</c>
/// (<c>NavAlongTrackUncertaintyM</c>) and the session's <c>nav.&lt;vehicle&gt;.*</c> stale flag.
/// </summary>
/// <param name="AlongTrackUncertaintyM">2σ along-track uncertainty for the marker tick; 0 hides the tick.</param>
/// <param name="Stale">No fix for longer than the stale time: the orbit chip gets a <c>STALE</c> badge.</param>
/// <param name="StaleAge">Age text for the badge (<c>34m</c>); empty shows the bare word.</param>
public sealed record MapTrackingOverlay(double AlongTrackUncertaintyM = 0, bool Stale = false, string StaleAge = "")
{
    public static MapTrackingOverlay None { get; } = new();
}
