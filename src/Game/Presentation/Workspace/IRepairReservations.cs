using Usa.Simulation.Engineering;

namespace Usa.Game.Presentation.Workspace;

/// <summary>
/// Parts a goal's current registration already holds in the spares (the simulation's ready <c>register_repair_design</c>
/// for that goal id). Re-registering a goal first returns its own reservation, so the workspace counts those parts
/// as available when the same goal is reopened or retested.
/// </summary>
public interface IRepairReservations
{
    /// <summary>Empty when <paramref name="goalId"/> has no registration (never registered, installed or abandoned).</summary>
    IReadOnlyList<PartQuantity> ReservedBy(string goalId);
}
