using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Flight;

/// <summary>
/// Accepts <c>set</c> effects on <c>flight.docked</c> (true docks, false undocks with the data's separation speed),
/// <c>flight.retrofire</c> (true fires the retro sequence), <c>flight.drogue_deployed</c> and <c>flight.main_deployed</c>
/// (true deploys; the deployment limits decide whether the chute survives). A dock or undock that flight refuses throws
/// <see cref="InvalidOperationException"/> instead of passing as a silent no-op; the composition root's docking
/// coordinator normally owns <c>flight.docked</c> and handles refusals itself.
/// </summary>
public sealed class FlightEffectHandler(FlightSystem flight) : IEffectHandler
{
    public const string Docked = "flight.docked";
    public const string Retrofire = "flight.retrofire";
    public const string DrogueDeployed = "flight.drogue_deployed";
    public const string MainDeployed = "flight.main_deployed";

    private readonly FlightSystem _flight = flight ?? throw new ArgumentNullException(nameof(flight));

    public string Prefix => StatePaths.Flight;

    public bool CanApply(string target, EffectOperation operation) =>
        operation == EffectOperation.Set && target is Docked or Retrofire or DrogueDeployed or MainDeployed;

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        var on = value.AsBool();
        switch (target)
        {
            case Docked when on:
                if (!_flight.Dock(now))
                    throw new InvalidOperationException($"Flight refused to dock ({target}): already docked, or a vehicle is not coasting.");
                break;
            case Docked:
                if (!_flight.Undock(now))
                    throw new InvalidOperationException($"Flight refused to undock ({target}): not docked, powered or landed.");
                break;
            case Retrofire when on:
                _flight.Retrofire(now);
                break;
            case DrogueDeployed when on:
                _flight.DeployChute(ChuteKind.Drogue, now);
                break;
            case MainDeployed when on:
                _flight.DeployChute(ChuteKind.Main, now);
                break;
        }
    }
}
