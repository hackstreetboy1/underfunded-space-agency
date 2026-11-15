using Usa.Simulation.Data;

namespace Usa.Simulation.Scoring;

/// <summary>Contents of <c>config/scoring.json</c> (schema <c>scoring/1</c>).</summary>
/// <param name="DebriefBestDecisions">How many good decisions the debrief highlights.</param>
/// <param name="OutsideZoneCapPoints">Score ceiling for a landing outside every recovery zone (100 = no cap).</param>
public sealed record ScoringConfig(
    string Schema,
    DimensionWeights Weights,
    CrewOutcomeTuning Crew,
    SafeReturnTuning SafeReturn,
    VehicleTuning Vehicle,
    DecisionTuning Decisions,
    IReadOnlyList<GradeBand> Grades,
    int DebriefBestDecisions,
    double OutsideZoneCapPoints) : IVersionedDefinition
{
    public const string SchemaTag = "scoring/1";
    public const string RelativePath = "config/scoring.json";
}

/// <summary>Points per dimension; they sum to 100.</summary>
public sealed record DimensionWeights(double Crew, double SafeReturn, double Objectives, double Vehicle, double Decisions);

/// <summary>Sub-weights sum to 1.</summary>
/// <param name="ComplicationThresholdPoints">Mission-caused health loss above this is a complication (GDD 10.3).</param>
/// <param name="ExposureZeroCreditS">Avoidable risk exposure at which the exposure line scores nothing.</param>
public sealed record CrewOutcomeTuning(
    double Survival,
    double HealthRetained,
    double WorstHealth,
    double Complications,
    double AvoidableExposure,
    double ComplicationThresholdPoints,
    double ExposureZeroCreditS);

/// <summary>Sub-weights sum to 1. Credit falls linearly between each full-credit and zero-credit value.</summary>
/// <param name="ZoneCredit">Credit (0-1) per recovery zone id.</param>
/// <param name="UnlistedZoneCredit">Credit for landing outside every listed zone.</param>
public sealed record SafeReturnTuning(
    double Landed,
    double EntryLoads,
    double RecoveryZone,
    double RecoveryWait,
    double Procedures,
    double EntryFullCreditG,
    double EntryZeroCreditG,
    double WaitFullCreditH,
    double WaitZeroCreditH,
    IReadOnlyDictionary<string, double> ZoneCredit,
    double UnlistedZoneCredit);

/// <summary>Sub-weights sum to 1.</summary>
public sealed record VehicleTuning(
    double CriticalSystems,
    double DamageContainment,
    double Redundancy,
    double RedundancyLostCredit,
    double DamagePenaltyPerEvent);

/// <summary>Sub-weights sum to 1.</summary>
public sealed record DecisionTuning(
    double PropellantDiscipline,
    double RiskAvoidance,
    double AvoidableLosses,
    double Procedures,
    double PropellantZeroCreditKg,
    double RiskZeroCreditS);

/// <summary>A grade starting at <paramref name="MinPoints"/> (inclusive) up to the next band.</summary>
public sealed record GradeBand(Grade Grade, string Label, int MinPoints);
