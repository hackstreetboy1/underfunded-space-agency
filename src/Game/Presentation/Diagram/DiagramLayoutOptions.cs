namespace Usa.Game.Presentation.Diagram;

/// <summary>Explicit layout for one compartment, overriding the bounds computed from its components.</summary>
public sealed record CompartmentLayout(string Id, string? Label = null, DiagramRect? Bounds = null);

/// <param name="CompartmentPaddingPx">Space between a compartment's outermost components and its border.</param>
/// <param name="NodeRadiusPx">Hit radius of a node.</param>
/// <param name="Compartments">Optional overrides (labels, bounds) by compartment id.</param>
public sealed record DiagramLayoutOptions(
    double CompartmentPaddingPx = 28,
    double NodeRadiusPx = 8,
    IReadOnlyList<CompartmentLayout>? Compartments = null)
{
    public static DiagramLayoutOptions Default { get; } = new();
}
