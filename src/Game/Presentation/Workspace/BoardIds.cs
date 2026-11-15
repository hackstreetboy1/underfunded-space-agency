namespace Usa.Game.Presentation.Workspace;

/// <summary>
/// A card on the workspace board. Unlike the assembly graph's instance ids, a card keeps its id when a removal is
/// undone, so views and undo history can hold on to it. The goal's fixed source and sink use reserved ids.
/// </summary>
public readonly record struct CardId(int Value) : IComparable<CardId>
{
    public static readonly CardId Source = new(-1);
    public static readonly CardId Sink = new(-2);

    public bool IsEndpoint => Value < 0;

    public int CompareTo(CardId other) => Value.CompareTo(other.Value);

    public override string ToString() => this == Source ? "source" : this == Sink ? "sink" : $"card {Value}";
}

/// <summary>One connector on the board: end <see cref="Port"/> of a card (0 left, 1 right), or port 0 of the source or sink.</summary>
public readonly record struct BoardPort(CardId Card, int Port)
{
    public static readonly BoardPort Source = new(CardId.Source, 0);
    public static readonly BoardPort Sink = new(CardId.Sink, 0);

    public override string ToString() => $"{Card}:{Port}";
}
