using Usa.Simulation.Kernel;

namespace Usa.Simulation.Engineering;

/// <param name="Count">Parts on board after the change.</param>
/// <param name="Available">Parts on board and not reserved by a pending task.</param>
public sealed record InventoryChanged(MissionTime Time, PartId Part, int Count, int Available) : ISimEvent;

/// <summary>
/// An effect took parts that were reserved for a pending repair (the count fell below the reservation, which shrank to fit).
/// Published after the matching <see cref="InventoryChanged"/>; the reservation holder decides which pending use loses them.
/// </summary>
/// <param name="Quantity">Reserved parts lost, at least 1.</param>
public sealed record InventoryReservationLost(MissionTime Time, PartId Part, int Quantity) : ISimEvent;
