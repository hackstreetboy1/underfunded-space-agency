using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Scenario.Model;

public sealed record Objective(
    ObjectiveId Id,
    ObjectiveKind Kind,
    string Title,
    double WeightPct,
    IExpression Complete,
    IReadOnlyList<FeasibilityRule> Feasibility);

public sealed record FeasibilityRule(string Id, ObjectiveState State, IExpression When, CausePolicy Cause, bool Latched);

/// <summary>Decides the cause of a loss at decision time (GDD 10.3).</summary>
/// <param name="AcceptableAlternative">When set, the loss is player_avoidable only if this is true at decision time.</param>
public sealed record CausePolicy(FeasibilityCause Cause, IExpression? AcceptableAlternative)
{
    public FeasibilityCause Decide(IStateQuery state)
    {
        if (AcceptableAlternative is null) return Cause;
        return AcceptableAlternative.IsTrue(state) ? FeasibilityCause.PlayerAvoidable : FeasibilityCause.Scenario;
    }
}
