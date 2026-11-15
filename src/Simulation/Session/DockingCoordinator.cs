using Usa.Simulation.Attitude;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Vehicle.Docking;

namespace Usa.Simulation.Session;

/// <summary>
/// The one place that docks and undocks (review X4). Handles the effect target <c>flight.docked</c> ahead of Flight's own
/// handler and moves the three areas together: Flight (orbits), Attitude (one rigid stack of the crewed and target vehicles;
/// a target without a selected control set gets <paramref name="targetControlSet"/>, its configured initial set, so it can
/// hold the stack) and the Vehicle docking ports (latch). A refused dock publishes <see cref="SessionEffectRefused"/>
/// (<c>dock_refused</c>). An undock first stops any session burn; if Flight is still powered it is retried every flight step
/// until it succeeds, so the undock always happens.
/// </summary>
internal sealed class DockingCoordinator(
    FlightSystem flight,
    AttitudeSystem attitude,
    DockingModel? docking,
    BurnController burns,
    EventBus events,
    string? targetControlSet) : IEffectHandler, ISimSystem, IHashContributor
{
    public const string Target = FlightEffectHandler.Docked;
    public const string DockRefused = "dock_refused";

    private bool _undockPending;

    public string Prefix => Target;

    public int PeriodTicks => 2;

    public bool UndockPending => _undockPending;

    public bool CanApply(string target, EffectOperation operation) => target == Target && operation == EffectOperation.Set;

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (!CanApply(target, operation)) throw new UnknownStatePathException(target);
        if (value.AsBool()) Dock(now);
        else RequestUndock(now);
    }

    public void Step(in StepContext context)
    {
        if (_undockPending) TryUndock(context.Now);
    }

    public void WriteHash(StateHashWriter writer) => writer.Write(_undockPending);

    private void Dock(MissionTime now)
    {
        _undockPending = false;
        if (flight.Docked) return;
        if (!flight.Dock(now))
        {
            events.Publish(new SessionEffectRefused(now, Target, DockRefused));
            return;
        }
        if (flight.TargetId is not { } targetId) return;
        if (attitude.CanDock(flight.CrewedId, targetId)) attitude.Dock(flight.CrewedId, targetId);
        docking?.Latch(now);
        var target = attitude.GetVehicle(targetId);
        if (target.ControlSetId.Length == 0 && targetControlSet is { Length: > 0 } set && target.ControlSetIds.Contains(set))
            attitude.SetControlSet(targetId, set, now);
    }

    private void RequestUndock(MissionTime now)
    {
        if (!flight.Docked) return;
        burns.Cancel(now);
        _undockPending = true;
        TryUndock(now);
    }

    private void TryUndock(MissionTime now)
    {
        if (!flight.Docked)
        {
            _undockPending = false;
            return;
        }
        if (!flight.Undock(now)) return;
        _undockPending = false;
        if (attitude.GetVehicle(flight.CrewedId).DockedWithId is not null) attitude.Undock(flight.CrewedId);
        docking?.Unlatch(now);
    }
}
