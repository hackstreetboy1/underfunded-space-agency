using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Systems;

/// <summary>
/// "Request…" on a crew-only control: a crew order pre-filled in the comms input for the player to review and
/// send. Nothing is executed until the crew is ordered.
/// </summary>
/// <param name="DesiredState">Null when the switch position is unknown and the draft asks for a report instead.</param>
/// <param name="Text">E.g. <c>Switch OAMS master power OFF</c>.</param>
public sealed record CrewOrderDraft(ComponentId ComponentId, DesiredControlState? DesiredState, string Text);
