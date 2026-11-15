namespace Usa.Game.Presentation.EntryHud;

/// <summary>
/// Everything the entry and landing HUD shows (UI 4.2), already formatted: top-left readouts (altitude, velocity,
/// flight-path angle, g gauge, heating bar, time to drogue/main altitudes), the bottom sequence strip, Entry Checklist
/// progress when authorized, and the crew health mini-bars. Every flight row carries its source, MET and knowledge state
/// (UI 2.3). Immutable; rebuild it at the UI cadence.
/// </summary>
/// <param name="Altitude">Cabin altimeter (Known) at or below its ceiling, else the ground prediction (EST).</param>
/// <param name="Velocity">Ground prediction.</param>
/// <param name="FlightPathAngle">Ground prediction.</param>
/// <param name="Checklist">Null unless the Entry Checklist is authorized.</param>
/// <param name="Corridor">The predicted entry angle against the corridor; null when the source gives no prediction.</param>
public sealed record EntryHudModel(
    HudReadoutRow Altitude,
    HudReadoutRow Velocity,
    HudReadoutRow FlightPathAngle,
    GLoadGauge GLoad,
    HeatingBar Heating,
    TimeToAltitude Drogue,
    TimeToAltitude Main,
    IReadOnlyList<SequenceStep> Sequence,
    string SequenceText,
    ChecklistProgress? Checklist,
    IReadOnlyList<CrewMini> Crew,
    CorridorBadge? Corridor = null)
{
    public const string DrogueLabel = "Drogue";
    public const string MainLabel = "Main";

    public string AltitudeText => Altitude.Text;

    public string VelocityText => Velocity.Text;

    public string FlightPathAngleText => FlightPathAngle.Text;

    /// <summary>The rows in display order, each with source, MET and state.</summary>
    public IReadOnlyList<HudReadoutRow> FlightRows => [Altitude, Velocity, FlightPathAngle, GLoad.Readout, Heating.Readout];

    /// <param name="words">Strings for the peak, corridor and predicted times (<see cref="EntryHudWords.From"/>); null leaves them empty.</param>
    public static EntryHudModel Build(IEntryHudSource source, EntryHudWords? words = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Build(source.Limits, source.Flight, source.Sequence, source.Checklist, source.Crew, words);
    }

    public static EntryHudModel Build(
        EntryHudLimits limits,
        in EntryFlightInputs flight,
        EntrySequenceInputs sequence,
        in EntryChecklistInputs checklist,
        IReadOnlyList<EntryCrewInputs> crew,
        EntryHudWords? words = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        words ??= EntryHudWords.None;
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(crew);

        var now = flight.NowMetS;
        var altitude = flight.DisplayedAltitude(limits.AltimeterCeilingM);
        var steps = SequenceStep.Build(sequence, words);
        return new EntryHudModel(
            HudReadoutRow.From(altitude, static m => EntryHudFormat.Number(m / 1000.0), " km", now),
            HudReadoutRow.From(flight.VelocityMps, EntryHudFormat.Number, " m/s", now),
            HudReadoutRow.From(flight.FlightPathAngleDeg, EntryHudFormat.AngleNumber, "°", now),
            GLoadGauge.From(flight.GLoad, limits, now, flight.PeakGLoad, words),
            HeatingBar.From(flight.HeatingProxy01, limits, now),
            DrogueSkipped(sequence)
                ? TimeToAltitude.Skipped(DrogueLabel, limits.DrogueDeployAltitudeM)
                : TimeToAltitude.Estimate(DrogueLabel, limits.DrogueDeployAltitudeM, sequence.Drogue, altitude, flight.VelocityMps, flight.FlightPathAngleDeg),
            TimeToAltitude.Estimate(MainLabel, limits.MainDeployAltitudeM, sequence.Main, altitude, flight.VelocityMps, flight.FlightPathAngleDeg),
            steps,
            SequenceStep.Join(steps),
            ChecklistProgress.From(checklist),
            CrewMinis(crew),
            flight.Corridor is { } corridor ? CorridorBadge.From(corridor, words, now) : null);
    }

    /// <summary>The drogue never went out and the main is deploying or deployed: the drogue row is superseded.</summary>
    public static bool DrogueSkipped(EntrySequenceInputs sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        return sequence.Drogue == EntryEventProgress.NotStarted
            && sequence.Main is EntryEventProgress.InProgress or EntryEventProgress.Done;
    }

    internal static CrewMini[] CrewMinis(IReadOnlyList<EntryCrewInputs> crew)
    {
        var minis = new CrewMini[crew.Count];
        for (var i = 0; i < minis.Length; i++) minis[i] = CrewMini.From(crew[i]);
        return minis;
    }
}
