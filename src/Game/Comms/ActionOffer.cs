using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>
/// GDD 5.2 recommendation request: CAPCOM asked whether to do something ("Should we jettison the equipment
/// section?"), and the crew asks "Want us to go ahead?". Nothing runs until CAPCOM accepts.
/// </summary>
/// <param name="Command">
/// The action as CAPCOM would order it, already checked against the phase allowlist and the action's parameters.
/// Accepting submits it under a new command id, unconfirmed, so a high-impact action still gets its confirmation.
/// </param>
/// <param name="Label">Button text naming the target, e.g. "Go ahead: Cycle OAMS thruster breakers T1–T4" (<see cref="ICommsSessionPort.DescribeCommand"/>).</param>
public sealed record ActionOffer(CommandRequest Command, CrewId Speaker, string Label) : CrewOffer(Speaker, Label);
