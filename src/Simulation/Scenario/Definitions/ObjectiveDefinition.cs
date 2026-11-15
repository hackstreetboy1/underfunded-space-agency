using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scenario.Definitions;

public sealed record ObjectiveDefinition(
    ObjectiveId Id,
    ObjectiveKind Kind,
    string Title,
    string Complete,
    double WeightPct = 0,
    IReadOnlyList<FeasibilityRuleDefinition>? Feasibility = null);

/// <param name="State"><c>impossible</c> or <c>unacceptable_risk</c>.</param>
/// <param name="AcceptableAlternative">
/// Evaluated when the rule first applies: true makes the loss <c>player_avoidable</c>, false <c>scenario</c>.
/// Requires <paramref name="Cause"/> = <c>player_avoidable</c>.
/// </param>
/// <param name="Latched">An unacceptable_risk that stays even after <paramref name="When"/> turns false.</param>
public sealed record FeasibilityRuleDefinition(
    string Id,
    ObjectiveState State,
    string When,
    FeasibilityCause Cause,
    string? AcceptableAlternative = null,
    bool Latched = false);
