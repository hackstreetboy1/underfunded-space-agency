using Usa.Simulation.Attitude;
using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Thermal;
using Usa.Simulation.Vehicle;

namespace Usa.Simulation.Session;

/// <summary>
/// Keeps the areas consistent after vehicle and flight events (SESSION_EFFECTS_NEEDED.md section 2):
/// <list type="bullet">
/// <item>A section separates: attitude mass properties follow and the section's thermal nodes detach. Its thrusters are no
/// longer available, so attitude stops commanding them. Flight reads the new mass at its next burn.</item>
/// <item>A retro rocket of the crewed vehicle ignites while flight has no burn scheduled: flight fires its retro sequence (data may also set
/// <c>flight.retrofire</c>; the second request is ignored).</item>
/// <item>Flight rejects a chute (too high or too fast): the vehicle chute is cut so both agree.</item>
/// </list>
/// Every second it also refreshes attitude mass properties from the vehicles (propellant use).
/// </summary>
internal sealed class VehicleEventCoordinator : ISimSystem
{
    private readonly VehicleSet _vehicles;
    private readonly AttitudeSystem _attitude;
    private readonly FlightSystem _flight;
    private readonly ThermalNetwork _thermal;
    private readonly Dictionary<ChuteKind, (int Vehicle, int Component)> _chutes = [];
    private readonly HashSet<ComponentId> _retroRockets = [];

    public VehicleEventCoordinator(VehicleSet vehicles, AttitudeSystem attitude, FlightSystem flight, ThermalNetwork thermal, EventBus events)
    {
        _vehicles = vehicles;
        _attitude = attitude;
        _flight = flight;
        _thermal = thermal;
        var crewed = vehicles.IndexOf(flight.CrewedId);
        var layout = vehicles[crewed].Layout;
        foreach (var rocket in layout.RetroRockets) _retroRockets.Add(layout.IdOf(rocket));
        var chutes = layout.Parachutes.ToArray();
        Array.Sort(chutes, (a, b) => layout[b].Parachute!.NominalDeployAltitudeKm.CompareTo(layout[a].Parachute!.NominalDeployAltitudeKm));
        if (chutes.Length >= 1) _chutes[ChuteKind.Drogue] = (crewed, chutes[0]);
        if (chutes.Length >= 2) _chutes[ChuteKind.Main] = (crewed, chutes[^1]);
        events.Subscribe(OnEvent);
    }

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    /// <summary>The vehicle parachute flight knows as <paramref name="kind"/> (highest nominal altitude is the drogue).</summary>
    public bool TryGetChute(ChuteKind kind, out VehicleState vehicle, out int component)
    {
        if (_chutes.TryGetValue(kind, out var chute))
        {
            vehicle = _vehicles[chute.Vehicle];
            component = chute.Component;
            return true;
        }
        vehicle = null!;
        component = -1;
        return false;
    }

    public void Step(in StepContext context)
    {
        for (var v = 0; v < _vehicles.Count; v++)
        {
            var mass = AttitudeSpecs.MassOf(_vehicles[v]);
            if (mass.IsValid) _attitude.SetMassProperties(_vehicles[v].VehicleId, mass);
        }
    }

    private void OnEvent(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case SectionJettisoned jettisoned:
                OnJettisoned(jettisoned);
                break;
            case ComponentStateChanged { Field: ComponentFields.Fired } fired when _retroRockets.Contains(fired.Component) && fired.Value.AsBool():
                if (_flight.ScheduledBurn is null) _flight.Retrofire(fired.Time);
                break;
            case ChuteDeployed { Result: not (ChuteDeployResult.Deployed or ChuteDeployResult.AlreadyDeployed) } failed
                when TryGetChute(failed.Chute, out var vehicle, out var chute):
                vehicle.SetParachute(chute, ParachuteStatus.Cut, failed.Time);
                break;
        }
    }

    private void OnJettisoned(SectionJettisoned jettisoned)
    {
        var vehicle = _vehicles.Get(jettisoned.VehicleId);
        var layout = vehicle.Layout;
        var section = layout.SectionIndex(jettisoned.SectionId);
        foreach (var node in layout.ThermalNodes)
        {
            if (layout.SectionOf(node) != section) continue;
            var thermalIndex = _thermal.IndexOfNode(layout.IdOf(node));
            if (thermalIndex >= 0) _thermal.Detach(thermalIndex);
        }
        var mass = AttitudeSpecs.MassOf(vehicle);
        if (mass.IsValid) _attitude.SetMassProperties(vehicle.VehicleId, mass);
    }
}
