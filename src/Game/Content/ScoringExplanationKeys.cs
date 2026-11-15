using Usa.Simulation.Scoring;

namespace Usa.Game.Content;

/// <summary>
/// Every explanation key the Scoring code can put on a <see cref="ScoreLine"/>, <see cref="ScoreNote"/>,
/// S+ reason or <see cref="DebriefItem"/>. Scenario decision keys (good decisions, avoidable losses) are
/// authored per mission and not listed. Usa.Content.Tests runs the scorer to keep this list complete.
/// </summary>
public static class ScoringExplanationKeys
{
    public const string HealthLossPrefix = "crew.health_loss.";
    public const string RecoveryZoneLine = "return.recovery_zone";
    public const string AvoidableLossNote = "decisions.avoidable_loss";
    public const string CrewLostNote = "crew.lost";
    public const string NotLandedNote = "return.not_landed";
    public const string ObjectivesNotApplicable = "objectives.not_applicable";

    /// <summary>Snake_case <c>HealthCause</c> values (Crew area); matched against <c>config/health.json</c> by the tests.</summary>
    public static IReadOnlyList<string> HealthCauses { get; } =
    [
        "ppco2", "low_ppo2", "low_pressure", "high_temperature", "low_temperature", "angular_rate",
        "g_load", "dehydration", "malnutrition", "fatigue", "injury",
    ];

    public static IReadOnlyList<string> Lines { get; } =
    [
        "crew.survival", "crew.health_retained", "crew.worst_health", "crew.complications", "crew.avoidable_exposure",
        "return.landed", "return.entry_loads", RecoveryZoneLine, "return.recovery_wait", "return.procedures",
        "objectives.completed", "objectives.abandoned", "objectives.lost_avoidable", "objectives.excluded_scenario",
        "objectives.fairness_full_credit", ObjectivesNotApplicable,
        "vehicle.critical_systems", "vehicle.damage_containment", "vehicle.redundancy",
        "decisions.propellant_discipline", "decisions.risk_avoidance", "decisions.avoidable_losses", "decisions.procedures",
    ];

    public static IReadOnlyList<string> Notes { get; } =
    [
        CrewLostNote, "crew.complication",
        .. HealthCauses.Select(cause => HealthLossPrefix + cause),
        NotLandedNote, "return.outside_recovery_zones", "return.equipment_not_jettisoned", "return.chutes_out_of_limits",
        "objectives.all_excluded_scenario", ObjectivesNotApplicable,
        "vehicle.uncontained_damage", "vehicle.redundancy.never_lost", "vehicle.redundancy.restored", "vehicle.redundancy.lost",
        AvoidableLossNote,
    ];

    public static IReadOnlyList<string> SPlus { get; } =
    [
        SPlusGate.PathAKey, SPlusGate.PathBKey, SPlusGate.CrewLostKey, SPlusGate.NotLandedKey, SPlusGate.ComplicationKey,
        SPlusGate.AbandonedKey, SPlusGate.AvoidableLossKey, SPlusGate.ScoreBelowBandKey, SPlusGate.NoObjectivesKey,
    ];

    /// <summary>Every key, each once, sorted ordinally.</summary>
    public static IReadOnlyList<string> All { get; } = Lines.Concat(Notes).Concat(SPlus).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
