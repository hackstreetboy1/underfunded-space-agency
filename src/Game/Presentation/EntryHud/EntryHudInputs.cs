namespace Usa.Game.Presentation.EntryHud;

/// <summary>Fixed numbers of the HUD (GDD 7.3: drogue ~15 km, main ~3.2 km, health loss above ~9 g sustained).</summary>
/// <param name="CrewCautionG">Lower tolerance mark on the g gauge (caution).</param>
/// <param name="CrewLimitG">Upper tolerance mark (health loss when sustained).</param>
/// <param name="HeatingCaution01">Heating proxy from which the bar reads caution.</param>
/// <param name="AltimeterCeilingM">Highest altitude the cabin altimeter reads; above it the altitude row is the ground prediction.</param>
public sealed record EntryHudLimits(
    double DrogueDeployAltitudeM = 15_000,
    double MainDeployAltitudeM = 3_200,
    double GaugeMaxG = 12,
    double CrewCautionG = 6,
    double CrewLimitG = 9,
    double HeatingCaution01 = 0.6,
    double HeatingCritical01 = 0.85,
    double AltimeterCeilingM = 30_000)
{
    public static EntryHudLimits Default { get; } = new();
}

/// <summary>
/// Flight readouts as the ground knows them (X8): each row carries value, band, source, MET and state. Speed,
/// flight-path angle and heating are always ground predictions; the g gauge is the telemetered accelerometer; altitude is
/// the cabin altimeter at or below its ceiling and the ground prediction above it (<see cref="DisplayedAltitude"/>).
/// </summary>
/// <param name="PredictedAltitudeM">The ground's predicted altitude, metres.</param>
/// <param name="CabinAltimeterM">The telemetered cabin altimeter, metres; Unknown when there is no reading.</param>
/// <param name="FlightPathAngleDeg">Negative while descending.</param>
/// <param name="HeatingProxy01">Predicted heat-rate proxy normalised to 0..1.</param>
/// <param name="NowMetS">Current MET, for the age on stale badges; null when not given.</param>
/// <param name="PeakGLoad">Highest accelerometer reading received so far (<see cref="PeakReadingTracker"/>); Unknown hides the peak.</param>
/// <param name="Corridor">The ground's predicted entry angle against the corridor; null hides the corridor badge.</param>
public readonly record struct EntryFlightInputs(
    HudReadout PredictedAltitudeM,
    HudReadout CabinAltimeterM,
    HudReadout VelocityMps,
    HudReadout FlightPathAngleDeg,
    HudReadout GLoad,
    HudReadout HeatingProxy01,
    double? NowMetS = null,
    HudReadout PeakGLoad = default,
    EntryCorridorInputs? Corridor = null)
{
    /// <summary>
    /// Compatibility for sources that only have plain values: every row becomes a ground prediction (EST) with no band
    /// or MET, and there is no altimeter reading. NaN gives an Unknown row.
    /// </summary>
    public EntryFlightInputs(double altitudeM, double velocityMps, double flightPathAngleDeg, double gLoad, double heatingProxy01)
        : this(
            HudReadout.GroundPrediction(altitudeM),
            HudReadout.Unknown(HudSources.CabinAltimeter),
            HudReadout.GroundPrediction(velocityMps),
            HudReadout.GroundPrediction(flightPathAngleDeg),
            HudReadout.GroundPrediction(gLoad),
            HudReadout.GroundPrediction(heatingProxy01))
    {
    }

    /// <summary>
    /// The altitude row: the cabin altimeter while it has a reading at or below <paramref name="altimeterCeilingM"/>,
    /// otherwise the ground prediction, which is never Known (only an instrument makes altitude Known).
    /// </summary>
    public HudReadout DisplayedAltitude(double altimeterCeilingM)
    {
        var altimeter = CabinAltimeterM;
        if (altimeter.State is ReadoutState.Known or ReadoutState.Stale && double.IsFinite(altimeter.Value) && altimeter.Value <= altimeterCeilingM)
            return altimeter;
        var predicted = PredictedAltitudeM;
        return predicted.State == ReadoutState.Known ? predicted with { State = ReadoutState.Estimated } : predicted;
    }
}

/// <summary>When a return event happened, as observed or reported, or when the ground predicts it.</summary>
/// <param name="Estimated">A prediction (shown <c>≈ 08:25</c>), not an observed time.</param>
public readonly record struct EntryStepTime(double MetS, bool Estimated = false);

/// <summary>The return sequence of GDD 7.3, one entry per strip step.</summary>
public sealed record EntrySequenceInputs(
    EntryEventProgress EquipmentSeparation,
    EntryEventProgress Retrofire,
    EntryEventProgress RetroSeparation,
    EntryEventProgress EntryInterface,
    EntryEventProgress Drogue,
    EntryEventProgress Main,
    EntryEventProgress Splashdown)
{
    public static EntrySequenceInputs NotStarted { get; } = new(
        EntryEventProgress.NotStarted, EntryEventProgress.NotStarted, EntryEventProgress.NotStarted,
        EntryEventProgress.NotStarted, EntryEventProgress.NotStarted, EntryEventProgress.NotStarted,
        EntryEventProgress.NotStarted);

    /// <summary>Event METs by step (observed, reported or predicted); a step without one shows no time.</summary>
    public IReadOnlyDictionary<EntryStep, EntryStepTime>? Times { get; init; }

    public EntryEventProgress this[EntryStep step] => step switch
    {
        EntryStep.EquipmentSeparation => EquipmentSeparation,
        EntryStep.Retrofire => Retrofire,
        EntryStep.RetroSeparation => RetroSeparation,
        EntryStep.EntryInterface => EntryInterface,
        EntryStep.Drogue => Drogue,
        EntryStep.Main => Main,
        EntryStep.Splashdown => Splashdown,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };
}

/// <summary>The Entry Checklist procedure (one crew task running the sequence at nominal altitudes).</summary>
/// <param name="Authorized">The player pre-authorized it; the HUD shows progress only then.</param>
public readonly record struct EntryChecklistInputs(bool Authorized, int CompletedSteps, int TotalSteps);

/// <summary>One astronaut for the health mini-bars.</summary>
/// <param name="Name">Display name, e.g. <c>Okafor</c>.</param>
/// <param name="HealthPercent">0..100; null when the ground does not know it.</param>
public sealed record EntryCrewInputs(string Name, double? HealthPercent);

/// <summary>Recovery after splashdown (GDD 7.3 step 7).</summary>
/// <param name="ZoneName">Recovery zone display name; null outside every zone.</param>
/// <param name="DistanceToShipKm">Capsule to recovery ship; null when unknown.</param>
/// <param name="RecoveryWaitSec">Estimated total wait from splashdown; null when unknown.</param>
/// <param name="RecoveryElapsedSec">MET seconds since splashdown.</param>
/// <param name="InZone">The landing estimate's or recovery report's zone verdict (never truth); null derives it from <paramref name="ZoneName"/>.</param>
/// <param name="SplashLongitudeDeg">Splash point as reported (east positive); null when not reported.</param>
/// <param name="SplashMetS">Splashdown MET as reported; null when not reported.</param>
public sealed record SplashdownInputs(
    bool SplashedDown,
    string? ZoneName,
    double? DistanceToShipKm,
    double? RecoveryWaitSec,
    double RecoveryElapsedSec,
    bool Recovered,
    bool? InZone = null,
    double? SplashLongitudeDeg = null,
    double? SplashMetS = null)
{
    public static SplashdownInputs None { get; } = new(false, null, null, null, 0, false);
}
