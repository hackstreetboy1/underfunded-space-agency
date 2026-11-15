namespace Usa.Game.Presentation.ViewModels;

/// <summary>How a readout value is known to the ground (GDD 4.1, UI.md 2.3).</summary>
public enum ReadoutKnowledge
{
    Unknown,
    Known,
    Stale,
    Estimated,
    Conflict,
    /// <summary>Coarse telemetry band (e.g. <c>LOW</c>), not a reading.</summary>
    GroundBand,
}
