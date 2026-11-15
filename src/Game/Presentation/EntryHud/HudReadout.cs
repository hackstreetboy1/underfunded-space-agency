namespace Usa.Game.Presentation.EntryHud;

/// <summary>
/// One HUD value as the ground knows it: best value, band, source, MET it was established and knowledge state. The
/// presentation never sees the true value; a measured row comes from a telemetered instrument, an estimate from a
/// ground prediction.
/// </summary>
/// <param name="Low">Lower edge of the band; equal to <paramref name="Value"/> for a measurement.</param>
/// <param name="High">Upper edge of the band.</param>
/// <param name="MetS">MET seconds when the value was measured or predicted; null when not known.</param>
public readonly record struct HudReadout(double Value, double Low, double High, string Source, double? MetS, ReadoutState State)
{
    /// <summary>True when the row has a number to show.</summary>
    public bool HasValue => State != ReadoutState.Unknown && double.IsFinite(Value);

    /// <summary>The value, or NaN when there is none.</summary>
    public double ValueOrNaN => HasValue ? Value : double.NaN;

    /// <summary>A telemetered instrument reading: Known while fresh, Stale after the fact's stale time.</summary>
    public static HudReadout Measured(double value, string source, double metS, bool stale = false) =>
        new(value, value, value, source, metS, stale ? ReadoutState.Stale : ReadoutState.Known);

    /// <summary>A model value with its band (shown with <c>≈</c>, the range and <c>EST</c>).</summary>
    public static HudReadout Estimate(double value, double low, double high, string source, double? metS) =>
        new(value, low, high, source, metS, ReadoutState.Estimated);

    /// <summary>The ground's prediction of a flight value with its band.</summary>
    public static HudReadout GroundPrediction(double value, double low, double high, double? metS) =>
        Estimate(value, low, high, HudSources.GroundPredict, metS);

    /// <summary>
    /// Compatibility for sources that only have a plain value: labelled as a ground prediction, EST, with no band or MET.
    /// NaN gives an Unknown row.
    /// </summary>
    public static HudReadout GroundPrediction(double value) =>
        double.IsFinite(value) ? Estimate(value, value, value, HudSources.GroundPredict, null) : Unknown(HudSources.GroundPredict);

    public static HudReadout Unknown(string source) => new(double.NaN, double.NaN, double.NaN, source, null, ReadoutState.Unknown);
}
