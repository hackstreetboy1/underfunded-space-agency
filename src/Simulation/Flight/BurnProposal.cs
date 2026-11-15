namespace Usa.Simulation.Flight;

/// <summary>
/// A proposed burn for the player to review. Never executed by the proposer.
/// <see cref="ExplanationKey"/> is a localisation key; <see cref="Arguments"/> fill its placeholders.
/// </summary>
/// <param name="Plan">Null when no burn achieves the goal.</param>
public sealed record BurnProposal(
    BurnGoal Goal,
    ManeuverPlan? Plan,
    ManeuverPreview? Preview,
    string ExplanationKey,
    IReadOnlyList<ExplanationArgument> Arguments)
{
    public bool HasPlan => Plan is not null;
}

public readonly record struct ExplanationArgument(string Name, double Value);
