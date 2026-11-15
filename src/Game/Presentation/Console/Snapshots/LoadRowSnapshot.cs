using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>A row in a bus's load list (UI.md 3.9, GDD 6.5).</summary>
/// <param name="PowerW">Demand when on.</param>
/// <param name="HeatW">Heat output when on.</param>
public sealed record LoadRowSnapshot(ComponentId Id, string Name, LoadPriorityGroup Priority, double PowerW, double HeatW)
{
    /// <summary>Known on/off state; null when unknown.</summary>
    public bool? IsOn { get; init; }

    /// <summary>Known condition; defaults to unknown.</summary>
    public ComponentCondition Condition { get; init; } = ComponentCondition.Unknown;

    /// <summary>Names of what depends on this load, for the tooltip.</summary>
    public IReadOnlyList<string> Dependents { get; init; } = [];

    /// <summary>The load's breaker, when it has one on this board.</summary>
    public ControlSnapshot? Switch { get; init; }
}
