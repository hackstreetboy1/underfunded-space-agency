using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>
/// One resource-strip tile. Readouts are already filtered to what the ground knows (GDD 4); the view models
/// also strip anything a knowledge state may not show, so an unknown readout never leaks a value.
/// </summary>
/// <param name="Id">Stable tile id, e.g. <c>o2</c>, <c>oams</c>.</param>
/// <param name="Primary">The main value (kg, kWh, L…), with trend.</param>
public sealed record ResourceSnapshot(string Id, string Label, ReadoutSnapshot Primary)
{
    /// <summary>Optional second value, e.g. O2 percent next to kg or Δv next to OAMS kg.</summary>
    public ReadoutSnapshot? Secondary { get; init; }

    /// <summary>Short state word, e.g. <c>SEALED</c> or <c>ARMED</c> for an RCS ring.</summary>
    public string? Detail { get; init; }

    /// <summary>Systems-board tab opened by clicking the tile.</summary>
    public SystemsTab Tab { get; init; } = SystemsTab.Power;

    /// <summary>Fact id the primary readout shows, the key of its Ask; falls back to the tile id when no fact backs it.</summary>
    public string? PrimaryFactKey { get; init; }

    /// <summary>Fact id the secondary readout shows, the key of its Ask.</summary>
    public string? SecondaryFactKey { get; init; }

    /// <summary>Pre-filled request for the "Ask" affordance, e.g. <c>Read OAMS</c>.</summary>
    public string? AskText { get; init; }
}
