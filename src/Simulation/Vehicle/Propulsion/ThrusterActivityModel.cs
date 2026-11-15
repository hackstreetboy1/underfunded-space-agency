using Usa.Simulation.Kernel;

namespace Usa.Simulation.Vehicle.Propulsion;

/// <summary>
/// Once per second, turns accumulated valve-open time into thruster activity and temperature, and group activity
/// (mean member activity, shown on Lodestar's status panel).
/// </summary>
/// <remarks>
/// Temperature relaxes first-order towards cold + (hot − cold) × activity with the thruster's time constant, using
/// the exact exponential step so the result does not depend on the step length.
/// </remarks>
public sealed class ThrusterActivityModel(VehicleState state) : ISimSystem
{
    private readonly VehicleState _state = state ?? throw new ArgumentNullException(nameof(state));

    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public void Step(in StepContext context)
    {
        var layout = _state.Layout;
        var dt = context.DeltaSeconds;
        Update(layout.Thrusters, dt);
        Update(layout.Engines, dt);

        foreach (var group in layout.ThrusterGroups)
        {
            var members = layout.MembersOf(group);
            var sum = 0.0;
            foreach (var member in members) sum += _state[member].Activity;
            _state.Mutable(group).Activity = members.Length > 0 ? sum / members.Length : 0;
        }
    }

    private void Update(ReadOnlySpan<int> thrusters, double dt)
    {
        var layout = _state.Layout;
        foreach (var thruster in thrusters)
        {
            ref var s = ref _state.Mutable(thruster);
            var activity = Math.Clamp(s.FiringSeconds / dt, 0, 1);
            s.FiringSeconds = 0;
            s.Activity = activity;

            var spec = layout.ThrusterSpecOf(thruster)!;
            var targetC = spec.ColdC + (spec.HotC - spec.ColdC) * activity;
            s.TemperatureC = targetC + (s.TemperatureC - targetC) * Math.Exp(-dt / spec.ThermalTimeConstantS);
        }
    }
}
