using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scenario.Model;

/// <summary>A validated mission with compiled expressions. Built by <see cref="ScenarioLoader"/>.</summary>
public sealed record Mission
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required int Era { get; init; }

    public required IReadOnlyList<string> Vehicles { get; init; }

    public required IReadOnlyList<CrewId> Crew { get; init; }

    public required MissionTime StartTime { get; init; }

    public required string StartPhase { get; init; }

    public required string EndedPhase { get; init; }

    public required IReadOnlyList<string> Phases { get; init; }

    public required IReadOnlyDictionary<string, string> Files { get; init; }

    public required IReadOnlyList<PhaseTransition> Transitions { get; init; }

    public required IReadOnlyList<MissionEventSpec> Events { get; init; }

    public required IReadOnlyList<NamedCondition> Conditions { get; init; }

    public required IReadOnlyList<Objective> Objectives { get; init; }

    public required IReadOnlyList<MissionRule> Rules { get; init; }

    public required IReadOnlyList<EndCondition> EndConditions { get; init; }

    public IReadOnlyList<MissionTimer> Timers { get; init; } = [];

    /// <summary>The crew locations as authored (validated by Session against the crew and vehicles); empty means the default pair.</summary>
    public IReadOnlyList<Definitions.MissionLocationDefinition> Locations { get; init; } = [];
}
