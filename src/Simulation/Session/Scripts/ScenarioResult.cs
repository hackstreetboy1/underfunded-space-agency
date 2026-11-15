using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;
using Usa.Simulation.Scoring;

namespace Usa.Simulation.Session.Scripts;

/// <summary>A notable moment of a played scenario.</summary>
public sealed record TimelineEntry(long MetMs, string Kind, string Text);

/// <summary>The crewed vehicle's state at one MET second (NaN where a path is not served).</summary>
/// <param name="Values">The mission's extra samples (<c>session.json</c> <c>report.samples</c>), in <see cref="ScenarioResult.SampleLabels"/> order.</param>
public sealed record ScenarioSample(
    long MetMs,
    string Phase,
    bool Docked,
    double RollDps,
    double TotalRateDps,
    double CabinTempC,
    double CabinPpCo2Mmhg,
    double AltitudeKm,
    double PeriapsisKm,
    IReadOnlyList<double> Values,
    IReadOnlyList<double> CrewHealth);

/// <summary>Headline numbers derived from the samples, for balancing.</summary>
/// <param name="IncidentMetS">When the mission's report incident event (<c>session.json</c> <c>report.incidentEvent</c>) fired (null: never or none).</param>
/// <param name="SecondsTo20Dps">Time from the incident until the crewed vehicle's |roll| first reached 20 °/s (null: never).</param>
/// <param name="SecondsTo120Dps">Time from the incident until |roll| first reached 120 °/s (null: never).</param>
/// <param name="UndockTo120DpsS">Time from the first undocking until |roll| first reached 120 °/s (null: never undocked or never reached).</param>
/// <param name="ValuesAtIncident">The extra samples at the last sample before the incident (the first sample when there was none).</param>
/// <param name="FinalValues">The extra samples at the end of the run.</param>
/// <param name="EstimateLandingZone">Where the aimed retrofire lands when flown on the ground's estimate (X8), <c>-</c> outside every zone; null when nothing was aimed.</param>
public sealed record ScenarioMetrics(
    double? IncidentMetS,
    double? SecondsTo20Dps,
    double? SecondsTo120Dps,
    double? UndockTo120DpsS,
    double MaxRollDps,
    IReadOnlyList<double> ValuesAtIncident,
    IReadOnlyList<double> FinalValues,
    IReadOnlyList<double> MinCrewHealth,
    double EntryPeakG,
    string LandingZone,
    double MissionMetS,
    string? EstimateLandingZone = null);

/// <summary>Estimated real play time under the automatic compression rules (GDD 3.2).</summary>
/// <param name="AutoRealS">Real seconds letting the director choose the rate everywhere.</param>
/// <param name="WithAdvanceRealS">The same, using "Advance to next event" whenever it is offered (1 real second per jump).</param>
/// <param name="ToSplashdownRealS">Auto real seconds up to splashdown (the recovery wait is presentation).</param>
/// <param name="AutoRealSByReason">Auto real seconds split by the compression director's reason (hold, task, coast...).</param>
/// <param name="ToEntryInterfaceRealS">Auto real seconds up to the entry interface event.</param>
public sealed record RealTimeEstimate(double AutoRealS, double WithAdvanceRealS, double ToSplashdownRealS, double ToEntryInterfaceRealS, IReadOnlyDictionary<string, double> AutoRealSByReason);

public sealed record ScenarioResult(
    ScenarioScript Script,
    string Ended,
    long EndMetMs,
    ScoreBreakdown Score,
    int Survivors,
    ScenarioMetrics Metrics,
    RealTimeEstimate RealTime,
    IReadOnlyList<TimelineEntry> Timeline,
    IReadOnlyList<ScenarioSample> Samples,
    IReadOnlyList<ScenarioSampleDefinition> SampleLabels,
    IReadOnlyDictionary<string, long> EventKeys,
    IReadOnlyList<string> Failures,
    CommandLog Log,
    IReadOnlyList<StateHashSample> Hashes,
    string FinalHash,
    IReadOnlyDictionary<string, string> Variants)
{
    public bool Passed => Failures.Count == 0;
}
