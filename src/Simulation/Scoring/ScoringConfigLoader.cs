using Usa.Simulation.Data;

namespace Usa.Simulation.Scoring;

/// <summary>Loads <c>config/scoring.json</c> and reports every invalid value at once.</summary>
public static class ScoringConfigLoader
{
    private const double SumTolerance = 1e-9;

    /// <param name="isKnownRecoveryZone">Flight's zone lookup; when given, zone credits must name real zones.</param>
    public static ScoringConfig Load(DefinitionLoader loader, Func<string, bool>? isKnownRecoveryZone = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var config = loader.Load<ScoringConfig>(ScoringConfig.RelativePath, ScoringConfig.SchemaTag);
        Validate(config, ScoringConfig.RelativePath, isKnownRecoveryZone).ThrowIfAny();
        return config;
    }

    public static DefinitionErrors Validate(ScoringConfig config, string file, Func<string, bool>? isKnownRecoveryZone = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new DefinitionErrors();

        var w = config.Weights;
        CheckSum(errors, file, "$.weights", 100.0, w.Crew, w.SafeReturn, w.Objectives, w.Vehicle, w.Decisions);

        var crew = config.Crew;
        CheckSum(errors, file, "$.crew", 1.0, crew.Survival, crew.HealthRetained, crew.WorstHealth, crew.Complications, crew.AvoidableExposure);
        CheckPositive(errors, file, "$.crew.exposureZeroCreditS", crew.ExposureZeroCreditS);
        if (crew.ComplicationThresholdPoints < 0)
            errors.Add(file, "$.crew.complicationThresholdPoints", "Must not be negative.");

        var ret = config.SafeReturn;
        CheckSum(errors, file, "$.safeReturn", 1.0, ret.Landed, ret.EntryLoads, ret.RecoveryZone, ret.RecoveryWait, ret.Procedures);
        CheckRamp(errors, file, "$.safeReturn.entry", ret.EntryFullCreditG, ret.EntryZeroCreditG);
        CheckRamp(errors, file, "$.safeReturn.wait", ret.WaitFullCreditH, ret.WaitZeroCreditH);
        CheckFraction(errors, file, "$.safeReturn.unlistedZoneCredit", ret.UnlistedZoneCredit);
        foreach (var zone in ret.ZoneCredit.Keys.Order(StringComparer.Ordinal))
        {
            CheckFraction(errors, file, $"$.safeReturn.zoneCredit.{zone}", ret.ZoneCredit[zone]);
            if (isKnownRecoveryZone is not null && !isKnownRecoveryZone(zone))
                errors.Add(file, $"$.safeReturn.zoneCredit.{zone}", $"Unknown recovery zone '{zone}'.");
        }

        var vehicle = config.Vehicle;
        CheckSum(errors, file, "$.vehicle", 1.0, vehicle.CriticalSystems, vehicle.DamageContainment, vehicle.Redundancy);
        CheckFraction(errors, file, "$.vehicle.redundancyLostCredit", vehicle.RedundancyLostCredit);
        CheckFraction(errors, file, "$.vehicle.damagePenaltyPerEvent", vehicle.DamagePenaltyPerEvent);

        var decisions = config.Decisions;
        CheckSum(errors, file, "$.decisions", 1.0,
            decisions.PropellantDiscipline, decisions.RiskAvoidance, decisions.AvoidableLosses, decisions.Procedures);
        CheckPositive(errors, file, "$.decisions.propellantZeroCreditKg", decisions.PropellantZeroCreditKg);
        CheckPositive(errors, file, "$.decisions.riskZeroCreditS", decisions.RiskZeroCreditS);

        if (config.DebriefBestDecisions is < 1 or > 5)
            errors.Add(file, "$.debriefBestDecisions", "Must be between 1 and 5.");

        if (config.OutsideZoneCapPoints is <= 0 or > 100)
            errors.Add(file, "$.outsideZoneCapPoints", "Must be above 0 and at most 100.");

        ValidateGrades(errors, file, config.Grades);
        return errors;
    }

    private static void ValidateGrades(DefinitionErrors errors, string file, IReadOnlyList<GradeBand> grades)
    {
        var tiers = Enum.GetValues<Grade>();
        if (grades.Count != tiers.Length)
        {
            errors.Add(file, "$.grades", $"Expected {tiers.Length} bands, one per grade, but found {grades.Count}.");
            return;
        }

        for (var i = 0; i < grades.Count; i++)
        {
            var band = grades[i];
            var path = $"$.grades[{i}]";
            if (band.Grade != tiers[i]) errors.Add(file, path, $"Expected grade '{tiers[i]}' at this position (lowest first).");
            if (string.IsNullOrWhiteSpace(band.Label)) errors.Add(file, path + ".label", "Label is empty.");
            if (i == 0 && band.MinPoints != 0) errors.Add(file, path + ".minPoints", "The lowest band must start at 0.");
            if (i > 0 && band.MinPoints <= grades[i - 1].MinPoints) errors.Add(file, path + ".minPoints", "Bands must rise strictly.");
            if (band.MinPoints > 100) errors.Add(file, path + ".minPoints", "Must not exceed 100.");
        }
    }

    private static void CheckSum(DefinitionErrors errors, string file, string path, double expected, params double[] parts)
    {
        var sum = 0.0;
        foreach (var part in parts)
        {
            if (part < 0) errors.Add(file, path, "Weights must not be negative.");
            sum += part;
        }
        if (Math.Abs(sum - expected) > SumTolerance * Math.Max(1.0, expected))
            errors.Add(file, path, $"Weights sum to {sum} but must sum to {expected}.");
    }

    private static void CheckRamp(DefinitionErrors errors, string file, string path, double full, double zero)
    {
        if (full < 0 || zero <= full) errors.Add(file, path, "Full-credit value must be non-negative and below the zero-credit value.");
    }

    private static void CheckPositive(DefinitionErrors errors, string file, string path, double value)
    {
        if (value <= 0) errors.Add(file, path, "Must be positive.");
    }

    private static void CheckFraction(DefinitionErrors errors, string file, string path, double value)
    {
        if (value is < 0 or > 1) errors.Add(file, path, "Must be between 0 and 1.");
    }
}
