using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <summary>Root of <c>missions/&lt;id&gt;/repairs.json</c>.</summary>
public sealed record RepairsFile(string Schema, IReadOnlyList<RepairGoalDefinition> Goals) : IVersionedDefinition;

/// <param name="SinkCandidates">Where the bypass ends, chosen at runtime: the first whose <c>when</c> holds.</param>
/// <param name="LoadCurrentA">Current the repaired circuit carries.</param>
/// <param name="WireRatingA">Rating of the wiring the protection must guard.</param>
/// <param name="MinLengthM">Shortest route that reaches from source to sink.</param>
public sealed record RepairGoalDefinition(
    string Id,
    string Name,
    RepairPortDefinition Source,
    IReadOnlyList<RepairSinkDefinition> SinkCandidates,
    double LoadCurrentA,
    double WireRatingA,
    double MinLengthM,
    RepairTaskParameters Task,
    string Description = "");

/// <param name="Label">Player-facing port name, e.g. "Main bus A aux 3".</param>
public sealed record RepairPortDefinition(ComponentId Component, string Label, string Connector);

/// <param name="Repairs">The damaged component the bypass replaces.</param>
/// <param name="When">Expression that selects this sink, e.g. <c>@tandem/rcs/harness_j14_a.condition == 'failed'</c>.</param>
public sealed record RepairSinkDefinition(ComponentId Component, string Label, string Connector, ComponentId Repairs, string When);

/// <summary>How a passing design becomes a crew task: duration = base + perPart × parts; complexity = base + parts / partsPerStep.</summary>
/// <param name="Skill">Crew skill domain used for duration and error modifiers.</param>
public sealed record RepairTaskParameters(
    TaskId TaskId,
    string Skill,
    double BaseDurationS,
    double DurationPerPartS,
    int BaseComplexity,
    int PartsPerComplexityStep,
    int MaxComplexity);
