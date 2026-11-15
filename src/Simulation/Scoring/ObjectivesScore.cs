namespace Usa.Simulation.Scoring;

/// <summary>
/// Mission objectives (GDD 10.1, 10.3). Objectives lost for scenario reasons leave the denominator, so
/// the remaining objectives share the full weight; abandoned and player-avoidable losses earn nothing.
/// A record with no objectives makes the dimension not applicable: it neither costs points nor opens S+
/// (see <see cref="SPlusGate"/>).
/// </summary>
internal static class ObjectivesScore
{
    public const string NotApplicableKey = "objectives.not_applicable";

    public static (DimensionScore Score, IReadOnlyList<ObjectiveResult> Results) Evaluate(double weightPoints, MissionRecord record)
    {
        var dimension = new DimensionBuilder(ScoreDimension.Objectives, weightPoints);
        if (record.Objectives.Count == 0)
        {
            dimension.Note(NotApplicableKey);
            dimension.Points(NotApplicableKey, weightPoints, weightPoints);
            return (dimension.Build(), []);
        }

        var denominator = 0.0;
        foreach (var objective in record.Objectives)
        {
            if (objective.WeightShare < 0)
                throw new ArgumentException($"Objective '{objective.Id}' has a negative weight share.", nameof(record));
            if (ObjectiveRules.CountsInDenominator(ObjectiveRules.Classify(objective))) denominator += objective.WeightShare;
        }

        var results = new List<ObjectiveResult>(record.Objectives.Count);
        foreach (var objective in record.Objectives)
        {
            var kind = ObjectiveRules.Classify(objective);
            var counted = ObjectiveRules.CountsInDenominator(kind);
            var maxPoints = counted && denominator > 0 ? weightPoints * objective.WeightShare / denominator : 0.0;
            var points = kind == ObjectiveClass.Completed ? maxPoints : 0.0;
            var key = kind switch
            {
                ObjectiveClass.Completed => "objectives.completed",
                ObjectiveClass.Abandoned => "objectives.abandoned",
                ObjectiveClass.LostAvoidable => "objectives.lost_avoidable",
                ObjectiveClass.LostScenario => "objectives.excluded_scenario",
                _ => throw new ArgumentOutOfRangeException(nameof(record), kind, "Unknown objective class."),
            };

            dimension.Points(key, points, maxPoints, objective.Id.Value);
            results.Add(new ObjectiveResult(objective.Id, objective.FinalState, objective.Cause, counted, points, maxPoints,
                ObjectiveRules.FinalMetS(objective)));
        }

        if (denominator <= 0)
        {
            // Every objective was taken away by the scenario: nothing was left for the player to lose.
            dimension.Note("objectives.all_excluded_scenario");
            dimension.Points("objectives.fairness_full_credit", weightPoints, weightPoints);
        }

        return (dimension.Build(), results);
    }
}
