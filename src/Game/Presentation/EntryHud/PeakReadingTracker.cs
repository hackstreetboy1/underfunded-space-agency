namespace Usa.Game.Presentation.EntryHud;

/// <summary>
/// The highest instrument reading the ground has received (e.g. the accelerometer's peak g): built from knowledge
/// readings only, never the true peak. Feed it each refresh's row; estimates and unknown rows are ignored.
/// </summary>
public sealed class PeakReadingTracker
{
    public PeakReadingTracker(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Peak = HudReadout.Unknown(source);
    }

    /// <summary>The highest reading so far, Known at the MET it was received; Unknown before the first.</summary>
    public HudReadout Peak { get; private set; }

    public HudReadout Observe(in HudReadout reading)
    {
        if (reading.State is not (ReadoutState.Known or ReadoutState.Stale) || !reading.HasValue) return Peak;
        if (!Peak.HasValue || reading.Value > Peak.Value) Peak = reading with { State = ReadoutState.Known };
        return Peak;
    }

    public void Reset() => Peak = HudReadout.Unknown(Peak.Source);
}
