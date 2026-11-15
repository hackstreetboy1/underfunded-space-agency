using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Diagram;

/// <summary>A line between two placed components, directed along the flow (source to consumer, sensor to observed).</summary>
/// <param name="Id">Stable id: <c>power:tandem/power/bus_a-&gt;tandem/oams/master</c>.</param>
/// <param name="Points">Polyline in diagram pixels: horizontal from the source, then vertical into the target.</param>
public sealed record DiagramConnection(
    string Id,
    ComponentId From,
    ComponentId To,
    ConnectionKind Kind,
    ConnectionStyle Style,
    IReadOnlyList<DiagramPoint> Points);
