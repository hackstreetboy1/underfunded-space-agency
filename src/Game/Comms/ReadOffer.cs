using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>A crew member's offer to read a fact they do not know yet (a <c>read_fact</c> glance, GDD 5.2 "offers to check").</summary>
/// <param name="Label">Button text, e.g. "Read OAMS propellant".</param>
public sealed record ReadOffer(FactId Fact, CrewId Reader, string Label) : CrewOffer(Reader, Label);
