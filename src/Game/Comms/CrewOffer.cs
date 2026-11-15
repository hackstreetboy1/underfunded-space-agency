using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>
/// A one-tap offer on a crew line (<see cref="TranscriptMessage.Offer"/>): the UI shows <see cref="Label"/> as a
/// button and accepts it with <see cref="CommsOrchestrator.AcceptOffer"/>. Accepting is CAPCOM's order; the
/// session validates it, and asks for confirmation, like any other.
/// </summary>
/// <param name="Speaker">The crew member who made the offer.</param>
/// <param name="Label">Button text, e.g. "Read OAMS propellant".</param>
public abstract record CrewOffer(CrewId Speaker, string Label);
