using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scenario.Definitions;

/// <summary>A latched flight rule: once <paramref name="When"/> is true it stays active for the rest of the mission.</summary>
/// <param name="AcceptableAlternative">Same meaning as on feasibility rules, evaluated at activation.</param>
public sealed record MissionRuleDefinition(
    string Id,
    string When,
    MissionRuleEffectDefinition Effect,
    FeasibilityCause Cause,
    string? AcceptableAlternative = null);

public sealed record MissionRuleEffectDefinition(
    ReturnRequirement? RequireReturn = null,
    IReadOnlyList<ObjectiveId>? ObjectivesUnacceptable = null);
