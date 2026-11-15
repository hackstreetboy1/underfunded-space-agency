using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Tasks;

/// <summary>A second task was rejected: the slot bar flashes once with <paramref name="Message"/>.</summary>
/// <param name="Message"><c>Crew busy: Okafor is on Cycle T5-T8 breakers</c>.</param>
public sealed record BusyFlash(string Message, CrewId? AssigneeId, TaskId? TaskId);
