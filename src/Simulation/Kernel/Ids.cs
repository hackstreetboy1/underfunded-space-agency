namespace Usa.Simulation.Kernel;

/// <summary>Validation shared by every typed id: non-empty snake_case segments, '/' separated.</summary>
public static class IdRules
{
    public static string Validate(string value, string kind)
    {
        if (string.IsNullOrEmpty(value)) throw new ArgumentException($"{kind} is empty.");
        foreach (var c in value)
        {
            var ok = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '/' or '.' or '-';
            if (!ok) throw new ArgumentException($"{kind} '{value}' contains '{c}'. Use lower-case snake_case.");
        }
        return value;
    }
}

/// <summary>A component in a vehicle, e.g. <c>tandem/oams/thruster_08</c>.</summary>
public readonly record struct ComponentId : IComparable<ComponentId>
{
    public ComponentId(string value) => Value = IdRules.Validate(value, nameof(ComponentId));
    public string Value { get; }
    public int CompareTo(ComponentId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>A reportable fact, e.g. <c>oams.prop_kg</c>.</summary>
public readonly record struct FactId : IComparable<FactId>
{
    public FactId(string value) => Value = IdRules.Validate(value, nameof(FactId));
    public string Value { get; }
    public int CompareTo(FactId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct ActionId : IComparable<ActionId>
{
    public ActionId(string value) => Value = IdRules.Validate(value, nameof(ActionId));
    public string Value { get; }
    public int CompareTo(ActionId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct TaskId : IComparable<TaskId>
{
    public TaskId(string value) => Value = IdRules.Validate(value, nameof(TaskId));
    public string Value { get; }
    public int CompareTo(TaskId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct CrewId : IComparable<CrewId>
{
    public CrewId(string value) => Value = IdRules.Validate(value, nameof(CrewId));
    public string Value { get; }
    public int CompareTo(CrewId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct ObjectiveId : IComparable<ObjectiveId>
{
    public ObjectiveId(string value) => Value = IdRules.Validate(value, nameof(ObjectiveId));
    public string Value { get; }
    public int CompareTo(ObjectiveId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct IncidentId : IComparable<IncidentId>
{
    public IncidentId(string value) => Value = IdRules.Validate(value, nameof(IncidentId));
    public string Value { get; }
    public int CompareTo(IncidentId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

public readonly record struct PartId : IComparable<PartId>
{
    public PartId(string value) => Value = IdRules.Validate(value, nameof(PartId));
    public string Value { get; }
    public int CompareTo(PartId other) => string.CompareOrdinal(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>Deterministic command id: "&lt;session&gt;-&lt;sequence&gt;". Also the AI request id.</summary>
public readonly record struct CommandId(string Value)
{
    public static CommandId Create(string sessionId, long sequence) => new($"{sessionId}-{sequence:D6}");
    public override string ToString() => Value;
}

/// <summary>Sequential observation id within a session.</summary>
public readonly record struct ObservationId(long Value)
{
    public override string ToString() => $"obs-{Value:D6}";
}
