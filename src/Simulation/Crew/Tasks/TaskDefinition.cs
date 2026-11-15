using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>Root of <c>catalog/tasks.json</c> (schema <c>tasks/1</c>).</summary>
public sealed record TaskCatalogDefinition : IVersionedDefinition
{
    public required string Schema { get; init; }

    public required IReadOnlyList<TaskDefinition> Tasks { get; init; }
}

/// <summary>
/// One crew task as written in data. Text fields marked as templates may contain <c>{param}</c>
/// placeholders, replaced by <see cref="TaskBinder"/> with argument values (and derived values).
/// </summary>
public sealed record TaskDefinition
{
    public required TaskId Id { get; init; }

    public required string Name { get; init; }

    public required TaskKind Kind { get; init; }

    /// <summary>Domains whose mean skill level sets duration and assignment order.</summary>
    public required IReadOnlyList<SkillDomain> Skills { get; init; }

    /// <summary>Duration for a healthy expert (skill 5) with standard equipment.</summary>
    public required double BaseDurationS { get; init; }

    public required int Complexity { get; init; }

    /// <summary>
    /// Where the assignee is while working: a <see cref="CrewLocations"/> id (mission 1: <c>cabin</c>, <c>eva</c>), or
    /// <see cref="CrewLocations.CurrentId"/> to stay where they are. Required in data.
    /// </summary>
    [JsonPropertyName("location")]
    public string LocationId { get; init; } = "";

    /// <summary>Coarse view of <see cref="LocationId"/>: <see cref="CrewLocation.Eva"/> for <c>eva</c>, otherwise a cabin.</summary>
    [JsonIgnore]
    public CrewLocation Location
    {
        get => LocationId == CrewLocations.EvaId ? CrewLocation.Eva : CrewLocation.Cabin;
        init => LocationId = value == CrewLocation.Eva ? CrewLocations.EvaId : CrewLocations.CabinId;
    }

    /// <summary>The assignee must be at one of these locations (their home location) to take the task; empty means anywhere.</summary>
    public IReadOnlyList<string> RequiredLocations { get; init; } = [];

    /// <summary>When the task succeeds the assignee stays at this location instead of returning home; null returns them home.</summary>
    public string? MovesTo { get; init; }

    public required bool Interruptible { get; init; }

    public double EquipmentFactor { get; init; } = 1.0;

    public IReadOnlyList<TaskParameterDefinition> Parameters { get; init; } = [];

    /// <summary>Checked before start (<c>inventory.&lt;part&gt;.count</c>) and consumed on success.</summary>
    public IReadOnlyList<TaskPartRequirement> RequiredParts { get; init; } = [];

    public IReadOnlyList<TaskPreconditionDefinition> Preconditions { get; init; } = [];

    public IReadOnlyList<TaskEffectDefinition> OnSuccess { get; init; } = [];

    public IReadOnlyList<TaskEffectDefinition> OnFailure { get; init; } = [];

    public IReadOnlyList<TaskEffectDefinition> OnStop { get; init; } = [];

    /// <summary>Observations recorded when the task succeeds.</summary>
    public IReadOnlyList<TaskObservationDefinition> Observations { get; init; } = [];
}

public sealed record TaskParameterDefinition
{
    public required string Name { get; init; }

    public required TaskParameterType Type { get; init; }

    /// <summary>Allowed values of a <see cref="TaskParameterType.Choice"/> parameter.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    public double? Min { get; init; }

    public double? Max { get; init; }

    /// <summary>
    /// Extra placeholders per choice value, e.g. group <c>t05_t08</c> → <c>{n1}</c> = <c>05</c>.
    /// Every value defines the same names.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Derived { get; init; } =
        ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty;
}

public sealed record TaskPartRequirement
{
    public required PartId Part { get; init; }

    public int Count { get; init; } = 1;
}

public sealed record TaskPreconditionDefinition
{
    /// <summary>Expression template that must be true for the task to start.</summary>
    public required string Condition { get; init; }

    /// <summary>Reason code reported when the condition is false.</summary>
    public required string Reason { get; init; }
}

/// <summary>An effect template: target path and value expression may contain placeholders.</summary>
public sealed record TaskEffectDefinition
{
    public required string Target { get; init; }

    public required EffectOperation Operation { get; init; }

    public required string Value { get; init; }
}

/// <summary>A fact template established by the task, with the components it covers.</summary>
public sealed record TaskObservationDefinition
{
    public required string Fact { get; init; }

    public required TaskObservationMethod Method { get; init; }

    public IReadOnlyList<string> Scope { get; init; } = [];
}
