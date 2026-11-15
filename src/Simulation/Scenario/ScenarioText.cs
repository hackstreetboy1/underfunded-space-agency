namespace Usa.Simulation.Scenario;

/// <summary>The snake_case text served in state paths; matches the JSON enum strings.</summary>
internal static class ScenarioText
{
    public static string Of(ObjectiveState state) => state switch
    {
        ObjectiveState.Achievable => "achievable",
        ObjectiveState.Completed => "completed",
        ObjectiveState.UnacceptableRisk => "unacceptable_risk",
        ObjectiveState.Impossible => "impossible",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static string Of(FeasibilityCause? cause) => cause switch
    {
        null => "",
        FeasibilityCause.Scenario => "scenario",
        FeasibilityCause.PlayerAvoidable => "player_avoidable",
        _ => throw new ArgumentOutOfRangeException(nameof(cause)),
    };

    public static string Of(MissionOutcomeKind? outcome) => outcome switch
    {
        null => "",
        MissionOutcomeKind.Recovered => "recovered",
        MissionOutcomeKind.AllCrewLost => "all_crew_lost",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    public static string Of(ReturnRequirement? requirement) => requirement switch
    {
        null => "",
        ReturnRequirement.NextSuitableZone => "next_suitable_zone",
        _ => throw new ArgumentOutOfRangeException(nameof(requirement)),
    };
}
