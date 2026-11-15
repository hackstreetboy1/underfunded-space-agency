using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Diagram;

/// <summary>
/// How the ground knows a component's condition (GDD 4.1, 4.4). Same four looks, with the same meaning, as the 3D
/// view's <c>Presentation.Rendering.ComponentLookState</c>; adapters map both explicitly from the knowledge base.
/// Staleness is not a look: it is <see cref="DiagramKnowledgeEntry.Stale"/>, so a stale confirmation keeps its look.
/// </summary>
public enum DiagramLookState
{
    Unknown,
    Suspected,
    ConfirmedNominal,
    ConfirmedDamaged,
}

/// <summary>One component's knowledge as the diagram reads it. Never simulation truth.</summary>
/// <param name="Variant">Confirmed damage variant (e.g. <c>stuck_open</c>), as in the 3D view; null otherwise.</param>
/// <param name="Stale">The confirmation is older than the fact's stale time; the diagram derives the age from <paramref name="ConfirmedAt"/>.</param>
/// <param name="ConfirmedAt">MET of the confirming report (GDD 4.4: "confirming report and MET").</param>
/// <param name="ConfirmedBy">Who or what confirmed it, e.g. <c>June Okafor · inspection</c>.</param>
public readonly record struct DiagramKnowledgeEntry(
    DiagramLookState State, string? Variant = null, bool Stale = false, MissionTime? ConfirmedAt = null, string? ConfirmedBy = null)
{
    public static DiagramKnowledgeEntry Unknown => default;
}
