namespace Usa.Simulation.Scoring;

/// <summary>
/// Grades a finished mission (GDD 10). Pure and deterministic: the same record always gives the same breakdown.
/// Its only input is <see cref="MissionRecord"/>, which has no guidance data, so hints cannot affect the grade (D13).
/// </summary>
public sealed class MissionScoring
{
    private readonly ScoringConfig _config;
    private readonly GradeTable _grades;

    public MissionScoring(ScoringConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _grades = new GradeTable(config.Grades);
    }

    public GradeTable Grades => _grades;

    public ScoreBreakdown Evaluate(MissionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.CrewCount == 0) throw new ArgumentException("A mission record needs at least one crew member.", nameof(record));

        var weights = _config.Weights;
        var (objectives, objectiveResults) = ObjectivesScore.Evaluate(weights.Objectives, record);
        DimensionScore[] dimensions =
        [
            CrewOutcomeScore.Evaluate(_config.Crew, weights.Crew, record),
            SafeReturnScore.Evaluate(_config.SafeReturn, weights.SafeReturn, record),
            objectives,
            VehicleConditionScore.Evaluate(_config.Vehicle, weights.Vehicle, record),
            DecisionQualityScore.Evaluate(_config.Decisions, weights.Decisions, record),
        ];

        var raw = 0.0;
        foreach (var dimension in dimensions) raw += dimension.Points;
        raw = Math.Clamp(raw, 0.0, 100.0);

        var survivors = record.SurvivorCount;
        var cap = 100.0 * survivors / record.CrewCount;
        if (record.Return is { Landed: true, RecoveryZoneId: null }) cap = Math.Min(cap, _config.OutsideZoneCapPoints);
        var final = Math.Min(raw, cap);
        var scorePoints = (int)Math.Floor(final);

        var gate = SPlusGate.Evaluate(record, _config.Crew.ComplicationThresholdPoints);
        var anyCrewLost = survivors < record.CrewCount;
        var grade = _grades.Lookup(final, gate.Eligible, anyCrewLost);

        return new ScoreBreakdown(
            dimensions,
            raw,
            cap,
            final,
            scorePoints,
            grade,
            _grades.Label(grade),
            ExplainSPlus(gate, grade),
            objectiveResults,
            BestDecisions(record),
            LargestAvoidableLoss(record, objectiveResults));
    }

    private static SPlusResult ExplainSPlus(SPlusAssessment gate, Grade grade)
    {
        var achieved = grade == Grade.SPlus;
        if (!gate.Eligible || achieved) return new SPlusResult(achieved, achieved ? gate.Path : SPlusPath.None, gate.Reasons);

        List<ScoreNote> reasons = [.. gate.Reasons, new ScoreNote(SPlusGate.ScoreBelowBandKey, null, null)];
        return new SPlusResult(false, SPlusPath.None, reasons);
    }

    private List<DebriefItem> BestDecisions(MissionRecord record)
    {
        var ranked = new List<WeightedDecision>(record.Decisions.GoodDecisions);
        ranked.Sort(static (a, b) =>
        {
            var byWeight = b.Weight.CompareTo(a.Weight);
            return byWeight != 0 ? byWeight : string.CompareOrdinal(a.Key, b.Key);
        });

        var count = Math.Min(_config.DebriefBestDecisions, ranked.Count);
        var best = new List<DebriefItem>(count);
        for (var i = 0; i < count; i++) best.Add(new DebriefItem(ranked[i].Key, null, ranked[i].Weight, ranked[i].MetS));
        return best;
    }

    private DebriefItem? LargestAvoidableLoss(MissionRecord record, IReadOnlyList<ObjectiveResult> objectives)
    {
        DebriefItem? largest = null;

        foreach (var loss in record.Decisions.AvoidableLosses)
        {
            var cost = DecisionQualityScore.LossCostPoints(_config.Decisions, _config.Weights.Decisions, loss);
            largest = Larger(largest, new DebriefItem(loss.Key, null, cost, loss.MetS));
        }

        foreach (var objective in objectives)
        {
            var avoidable = objective.FinalState == ObjectiveFinalState.Abandoned
                            || (objective.Cause == ObjectiveLossCause.PlayerAvoidable && objective.FinalState != ObjectiveFinalState.Completed);
            if (!avoidable) continue;

            var key = objective.FinalState == ObjectiveFinalState.Abandoned ? "objectives.abandoned" : "objectives.lost_avoidable";
            largest = Larger(largest, new DebriefItem(key, objective.Id.Value, objective.MaxPoints, objective.FinalMetS));
        }

        return largest;
    }

    private static DebriefItem Larger(DebriefItem? current, DebriefItem candidate)
    {
        if (current is null || candidate.Magnitude > current.Magnitude) return candidate;
        if (candidate.Magnitude < current.Magnitude) return current;
        var byKey = string.CompareOrdinal(candidate.Key, current.Key);
        if (byKey != 0) return byKey < 0 ? candidate : current;
        return string.CompareOrdinal(candidate.Subject, current.Subject) < 0 ? candidate : current;
    }
}
