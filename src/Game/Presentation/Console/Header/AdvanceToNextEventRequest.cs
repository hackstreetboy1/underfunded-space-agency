using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Header;

/// <summary>The player pressed "Advance to next event" (or <c>N</c>) while it was enabled.</summary>
public sealed record AdvanceToNextEventRequest(MissionTime RequestedAt);
