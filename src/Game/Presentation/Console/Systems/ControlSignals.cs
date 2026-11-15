namespace Usa.Game.Presentation.Console.Systems;

/// <summary>Where every control on the board sends its requests; the console wires these to the command pipeline.</summary>
public sealed class ControlSignals
{
    /// <summary>Run this remote operation (already confirmed when it needed to be).</summary>
    public Signal<RemoteControlIntent> ExecuteRequested { get; } = new();

    /// <summary>Show the confirmation dialog for a guarded control.</summary>
    public Signal<ConfirmationRequest> ConfirmationRequested { get; } = new();

    /// <summary>Pre-fill a crew order for a crew-only control.</summary>
    public Signal<CrewOrderDraft> CrewOrderRequested { get; } = new();
}
