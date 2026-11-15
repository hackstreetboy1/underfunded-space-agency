using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Systems;

/// <summary>A remote control operation for the command pipeline (source <c>RemoteControl</c>).</summary>
/// <param name="Confirmed">The player accepted the confirmation of a guarded control.</param>
public sealed record RemoteControlIntent(ComponentId ComponentId, DesiredControlState DesiredState, bool Confirmed);
