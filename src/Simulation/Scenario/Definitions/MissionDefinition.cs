using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scenario.Definitions;

/// <summary>Root of <c>missions/&lt;id&gt;/mission.json</c>, schema <c>mission/1</c>. Expressions are source text.</summary>
/// <param name="Files">Sibling data files by role; <c>incidents</c> is required.</param>
/// <param name="EndedPhase">The phase entered when an end condition fires.</param>
/// <param name="Conditions">Named reusable conditions served as <c>mission.condition.&lt;id&gt;</c>.</param>
/// <param name="Timers">Named accumulators and deadlines served as <c>mission.timer.&lt;id&gt;.*</c>.</param>
/// <param name="Locations">Where the crew can be (cabins and suited places); Session turns them into the roster's crew locations. Null: one cabin in the crewed vehicle plus a suited <c>eva</c>.</param>
public sealed record MissionDefinition(
    string Schema,
    string Id,
    string Title,
    int Era,
    IReadOnlyList<string> Vehicles,
    IReadOnlyList<CrewId> Crew,
    MissionStartDefinition Start,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<string> Phases,
    string EndedPhase,
    IReadOnlyList<PhaseTransitionDefinition> PhaseTransitions,
    IReadOnlyList<MissionEventDefinition> Events,
    IReadOnlyList<ObjectiveDefinition> Objectives,
    IReadOnlyList<EndConditionDefinition> EndConditions,
    IReadOnlyList<NamedConditionDefinition>? Conditions = null,
    IReadOnlyList<MissionRuleDefinition>? MissionRules = null,
    IReadOnlyList<MissionTimerDefinition>? Timers = null,
    IReadOnlyList<MissionLocationDefinition>? Locations = null) : IVersionedDefinition;

public sealed record MissionStartDefinition(double MetS, string Phase);

/// <summary>Moves from any phase in <paramref name="From"/> on a mission event (raised since the phase began) or a condition.</summary>
public sealed record PhaseTransitionDefinition(IReadOnlyList<string> From, string To, string? Event = null, string? When = null);

/// <summary>A registry entry. <paramref name="When"/> raises it automatically; <paramref name="Source"/> documents who raises it otherwise.</summary>
public sealed record MissionEventDefinition(string Id, string? When = null, string? Source = null);

public sealed record NamedConditionDefinition(string Id, string When);

/// <summary>
/// Accumulates mission time while it runs: after <paramref name="StartEvent"/> (if any), until <paramref name="StopEvent"/> (if any),
/// while <paramref name="RunWhile"/> holds (if given). <paramref name="ResetWhen"/> zeroes it. With <paramref name="DurationS"/> it is
/// also a deadline that expires once, raising <paramref name="ExpireEvent"/> when given.
/// </summary>
public sealed record MissionTimerDefinition(
    string Id,
    string? RunWhile = null,
    string? ResetWhen = null,
    string? StartEvent = null,
    string? StopEvent = null,
    double? DurationS = null,
    string? ExpireEvent = null);

public sealed record EndConditionDefinition(string Id, MissionOutcomeKind Outcome, string? Event = null, string? When = null);

/// <summary>
/// One place the crew can be (<c>mission.json</c> <c>locations</c>): inside the cabin of vehicle <paramref name="Cabin"/>, or
/// suited (no cabin) tethered to <paramref name="Vehicle"/>. Exactly one location is the <paramref name="Default"/>.
/// </summary>
/// <param name="AngularRateFactor">How much more strongly than the vehicle's own rotation a member here feels it (EVA: 2).</param>
public sealed record MissionLocationDefinition(
    string Id,
    string? Cabin = null,
    string? Vehicle = null,
    bool Default = false,
    double AngularRateFactor = 1.0);
