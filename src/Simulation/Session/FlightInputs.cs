using Usa.Simulation.Flight;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>
/// What flight reads from the vehicle and attitude models. Mass is the vehicle's current mass (sections, tanks, unburnt retro
/// propellant). Thrust comes only from <see cref="BurnController"/>'s session burns: the small net force of attitude thrusters
/// (including a stuck one) is not propagated into the orbit, so flight stays analytic while coasting and an undock is never
/// refused because a stuck thruster keeps flight powered (review X4).
/// </summary>
internal sealed class FlightInputs(VehicleSet vehicles) : IFlightInputs
{
    public BurnController? Burns { get; set; }

    public double MassKg(string vehicleId) => vehicles.Get(vehicleId).ComputeMassProperties().MassKg;

    public PoweredInput SampleThrust(in StepContext context, in FlightKinematics kinematics) =>
        Burns?.Sample(context, kinematics) ?? PoweredInput.Idle;

    public double PointingErrorRad(in StepContext context) => Burns?.ProgrammedPointingErrorRad() ?? 0;
}
