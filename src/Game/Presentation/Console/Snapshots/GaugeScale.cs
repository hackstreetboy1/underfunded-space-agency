namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>
/// The arc-gauge scale of a readout (UI.md 3.9, Prop &amp; attitude rate gauges): the arc spans zero to
/// <paramref name="Max"/> in magnitude; the bands come from documented limits the ground knows (data), never from truth.
/// </summary>
/// <param name="CautionAt">Magnitude from which the reading is a caution; null for no caution band.</param>
/// <param name="CriticalAt">Magnitude from which the reading is critical; null for no critical band.</param>
public sealed record GaugeScale(double Max, double? CautionAt = null, double? CriticalAt = null);
