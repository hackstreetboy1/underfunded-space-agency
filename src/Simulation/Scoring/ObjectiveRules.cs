namespace Usa.Simulation.Scoring;

internal enum ObjectiveClass
{
    Completed,
    Abandoned,
    LostScenario,
    LostAvoidable,
}

/// <summary>One shared classification so the objectives dimension and the S+ gate never disagree.</summary>
internal static class ObjectiveRules
{
    public static ObjectiveClass Classify(ObjectiveRecord objective) => objective.FinalState switch
    {
        ObjectiveFinalState.Completed => ObjectiveClass.Completed,
        ObjectiveFinalState.Abandoned => ObjectiveClass.Abandoned,
        ObjectiveFinalState.Impossible or ObjectiveFinalState.UnacceptableRisk => objective.Cause switch
        {
            ObjectiveLossCause.Scenario => ObjectiveClass.LostScenario,
            ObjectiveLossCause.PlayerAvoidable => ObjectiveClass.LostAvoidable,
            _ => throw new ArgumentException(
                $"Objective '{objective.Id}' is {objective.FinalState} without a cause.", nameof(objective)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(objective), objective.FinalState, "Unknown objective state."),
    };

    public static bool CountsInDenominator(ObjectiveClass kind) => kind != ObjectiveClass.LostScenario;

    public static double? FinalMetS(ObjectiveRecord objective) =>
        objective.Transitions.Count > 0 ? objective.Transitions[^1].MetS : null;
}
