using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>Everything one systems-board tab shows. A tab without a snapshot shows nothing but its alarms.</summary>
public sealed record SystemsGroupSnapshot(SystemsTab Tab)
{
    public IReadOnlyList<ReadoutGroupSnapshot> ReadoutGroups { get; init; } = [];

    public IReadOnlyList<LoadRowSnapshot> Loads { get; init; } = [];

    public IReadOnlyList<ControlSnapshot> Controls { get; init; } = [];
}

/// <summary>A titled group of readouts, e.g. <c>Main bus A</c>.</summary>
public sealed record ReadoutGroupSnapshot(string Id, string Title, IReadOnlyList<SystemReadoutSnapshot> Readouts);

/// <param name="Key">Stable key within the board, usually the fact id (<c>oams.prop_kg</c>).</param>
public sealed record SystemReadoutSnapshot(string Key, string Label, ReadoutSnapshot Value)
{
    /// <summary>Pre-filled request for the "Ask" affordance.</summary>
    public string? AskText { get; init; }

    /// <summary>Draw as an arc gauge on this scale (UI.md 3.9 rate gauges); null for a plain readout row.</summary>
    public GaugeScale? Gauge { get; init; }
}
