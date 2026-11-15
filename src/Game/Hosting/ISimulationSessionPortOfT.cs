namespace Usa.Game.Hosting;

/// <summary>
/// The full session port including commands and snapshots. Generic because command, result and snapshot types
/// live in simulation areas the hosting layer does not compile against; the adapter closes it as
/// <c>ISimulationSessionPort&lt;CommandRequest, ValidationResult, TSnapshot&gt;</c>.
/// </summary>
public interface ISimulationSessionPort<in TCommand, out TResult, out TSnapshot> : ISimulationSessionPort
{
    /// <summary>Validates and, if accepted, applies a command at the current MET.</summary>
    TResult Submit(TCommand request);

    /// <summary>Read-only view of session state for presentation.</summary>
    TSnapshot Snapshot();
}
