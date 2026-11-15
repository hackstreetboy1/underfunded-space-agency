using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Diagram;

/// <summary>A node with its knowledge-derived display and interaction state.</summary>
/// <param name="Highlighted">On the selected load's supply path.</param>
public sealed record DiagramNodeView(DiagramNode Node, NodeDisplay Display, bool Selected, bool Highlighted, bool Hovered);

/// <param name="Highlighted">On the selected load's supply path.</param>
public sealed record DiagramConnectionView(DiagramConnection Connection, bool Highlighted);

/// <summary>Everything a Godot <c>_Draw</c> needs for the diagram: only visible compartments, nodes and lines.</summary>
/// <param name="SupplyPath">The selected component's supply path; null when nothing is selected or it has no supply.</param>
public sealed record DiagramFrame(
    IReadOnlyList<DiagramCompartment> Compartments,
    IReadOnlyList<DiagramNodeView> Nodes,
    IReadOnlyList<DiagramConnectionView> Connections,
    DiagramLayers Layers,
    ComponentId? Selected,
    SupplyPath? SupplyPath);
