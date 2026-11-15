namespace Usa.Simulation.Engineering;

/// <summary>A part placed on the workspace board. Placed parts count up from 1; the goal's fixed ports use reserved ids.</summary>
public readonly record struct PartInstanceId(int Value) : IComparable<PartInstanceId>
{
    public static readonly PartInstanceId Source = new(-1);
    public static readonly PartInstanceId Sink = new(-2);

    public bool IsEndpoint => Value < 0;

    public int CompareTo(PartInstanceId other) => Value.CompareTo(other.Value);

    public override string ToString() => this == Source ? "source" : this == Sink ? "sink" : $"part {Value}";
}

/// <summary>One connector: end <see cref="Port"/> (0 or 1) of a placed part, or port 0 of the source or sink.</summary>
public readonly record struct PortRef(PartInstanceId Instance, int Port)
{
    public static readonly PortRef Source = new(PartInstanceId.Source, 0);
    public static readonly PortRef Sink = new(PartInstanceId.Sink, 0);

    public override string ToString() => $"{Instance}:{Port}";
}

public sealed record PlacedPart(PartInstanceId Instance, PartDefinition Part);
