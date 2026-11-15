using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Workspace;

/// <summary>The workspace header line (UI 4.6): goal, fixed ports and electrical limits, formatted per UI 6.2.</summary>
/// <param name="Summary">e.g. <c>Source: Main bus A aux 3 (D-type) → Sink: RCS ring A driver input (round) · Load 7.5 A</c>.</param>
public sealed record GoalHeader(
    string GoalId,
    string Name,
    string Description,
    PortSummary Source,
    PortSummary Sink,
    string LoadText,
    string WireRatingText,
    string MinLengthText,
    string Summary);

/// <param name="ConnectorId">Connector type id, also the key of its shape icon (<c>d_type</c>, <c>round</c>).</param>
public sealed record PortSummary(ComponentId Component, string Label, string ConnectorId, string ConnectorName);
