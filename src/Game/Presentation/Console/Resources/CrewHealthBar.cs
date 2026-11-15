using Usa.Game.Presentation.Console.Crew;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>A mini health bar on the Crew tile.</summary>
public sealed record CrewHealthBar(CrewId Id, string Name, CrewHealthView Health);
