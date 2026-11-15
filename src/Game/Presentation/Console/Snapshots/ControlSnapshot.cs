using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>A switch or button on the systems board.</summary>
/// <param name="Label">For switches the thing switched (<c>OAMS master power</c>); for buttons the imperative action (<c>Purge fuel cell 1</c>).</param>
public sealed record ControlSnapshot(ComponentId ComponentId, string Label, ControlAuthority Authority)
{
    public ControlKind Kind { get; init; } = ControlKind.Switch;

    /// <summary>Switch position; null when the ground does not know it.</summary>
    public bool? IsOn { get; init; }

    /// <summary>Critical load: a cover must be opened, then the change confirmed.</summary>
    public bool Guarded { get; init; }

    /// <summary>False greys the control out (e.g. no power to the relay).</summary>
    public bool Enabled { get; init; } = true;

    public string? DisabledReason { get; init; }

    /// <summary>Position word for on, e.g. <c>OPEN</c>; null or empty uses the console's own word.</summary>
    public string? OnLabel { get; init; }

    /// <summary>Position word for off; null or empty uses the console's own word.</summary>
    public string? OffLabel { get; init; }

    /// <summary>Effects listed in the confirmation dialog of a guarded control (UI.md 5.2), already in words.</summary>
    public IReadOnlyList<string> Effects { get; init; } = [];

    /// <summary>What switching on (or pressing a button) does: the action's <c>impact.*</c> keys and irreversible flag.</summary>
    public ControlImpact OnImpact { get; init; } = ControlImpact.None;

    /// <summary>What switching off does.</summary>
    public ControlImpact OffImpact { get; init; } = ControlImpact.None;
}
