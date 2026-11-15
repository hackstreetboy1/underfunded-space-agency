using Usa.Simulation.Kernel;

namespace Usa.Simulation.Flight;

/// <summary>What flight needs from the vehicle and attitude models each step. Adapted by the composition root.</summary>
public interface IFlightInputs
{
    /// <summary>
    /// Current mass of a vehicle in kg (one of the mission's <see cref="FlightVehicles.Ids"/>), positive and finite. The vehicle area owns propellant mass; see <see cref="BurnEnded"/> for what it must deduct.
    /// </summary>
    double MassKg(string vehicleId);

    /// <summary>
    /// Thrust the crewed vehicle (or docked stack) produces over the coming step from engines flight does not
    /// schedule itself (translation thrusters, a docked target's main engine). Return <see cref="PoweredInput.Idle"/> when nothing fires.
    /// Every number must be finite. Propellant for this thrust is drawn by whoever supplies it (reported back as
    /// <see cref="BurnEnded.ExternalPropellantKg"/> for information only).
    /// </summary>
    PoweredInput SampleThrust(in StepContext context, in FlightKinematics kinematics);

    /// <summary>
    /// Signed in-plane pointing error (rad) of the crewed vehicle, applied to programmed burns such as the retrofire:
    /// <c>Angles.Wrap(actualThrustAxisAngle − commandedBurnAngle)</c>, positive counter-clockwise (+X towards +Y),
    /// in (−π, π]. Attitude's unsigned <c>error_deg</c> is not suitable; use <c>AttitudeVehicle.SignedErrorDeg</c>
    /// (hold target as the burn direction) or wrap <c>pointing_deg</c> minus the burn direction. Must be finite.
    /// </summary>
    double PointingErrorRad(in StepContext context);
}
