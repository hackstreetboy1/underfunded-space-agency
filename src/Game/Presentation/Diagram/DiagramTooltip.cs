using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Diagram;

/// <summary>Hover tooltip: name, family, known state, its confirmation and dependencies.</summary>
/// <param name="ConfirmedBy">Who or what confirmed the state (GDD 4.4); null when unconfirmed.</param>
/// <param name="ConfirmedAt">MET of that confirmation; null when unconfirmed.</param>
/// <param name="Feeds">e.g. <c>Feeds: Main bus A → Cabin fan, Comms transceiver</c>; null when it feeds nothing.</param>
/// <param name="FedBy">e.g. <c>Fed by: Main bus A</c>; null when nothing feeds it.</param>
public sealed record DiagramTooltip(
    string Name, string Family, string KnownState, string? Badge, string? Feeds, string? FedBy,
    string? ConfirmedBy = null, MissionTime? ConfirmedAt = null);

/// <summary>What a pointer is over.</summary>
public readonly record struct DiagramHit(ComponentId? Component, string? CompartmentId)
{
    public bool IsEmpty => Component is null && CompartmentId is null;
}
