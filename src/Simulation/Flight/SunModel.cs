using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// The Sun as flight sees it (M2 §16 E4): a fixed inertial Sun line and the body's cylindrical shadow around each flown
/// vehicle's position, with a linear penumbra ramp. Every value is a pure function of the current flight state, so nothing
/// here is hashed. Served as <c>flight.sun_deg</c>, <c>flight.sunlit</c>, <c>flight.sunlit_fraction</c> (crewed vehicle or
/// stack) and <c>flight.&lt;v&gt;.sunlit</c>, <c>flight.&lt;v&gt;.sunlit_fraction</c> per flown vehicle.
/// </summary>
public sealed class SunModel
{
    /// <summary>A vehicle counts as sunlit from the middle of the penumbra (the geometric shadow edge) outwards.</summary>
    public const double SunlitThreshold = 0.5;

    private readonly FlightSystem _flight;
    private readonly Vec2d _sunLine;

    public SunModel(SunDefinition definition, FlightSystem flight)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _flight = flight ?? throw new ArgumentNullException(nameof(flight));
        var angle = definition.InertialAngleDeg * Angles.DegToRad;
        _sunLine = new Vec2d(Math.Cos(angle), Math.Sin(angle));
        SunInertial = new Vec3d(_sunLine.X, _sunLine.Y, 0);
        SunDeg = Angles.WrapPositive(angle) * Angles.RadToDeg;
    }

    public SunDefinition Definition { get; }

    /// <summary>Inertial direction to the Sun, [0, 360), same convention as <c>attitude.&lt;v&gt;.pointing_deg</c>.</summary>
    public double SunDeg { get; }

    /// <summary>Unit vector to the Sun in inertial axes (in the orbit plane).</summary>
    public Vec3d SunInertial { get; }

    /// <summary>Sunlit fraction of the crewed vehicle, or the docked stack.</summary>
    public double CrewedSunlitFraction() => SunlitFraction(_flight.Crewed);

    /// <summary>
    /// Sunlit fraction of a flown vehicle: the crewed one (or the stack, which carries a docked target); the target flies its
    /// own trajectory while undocked. A vehicle flight does not propagate gets the crewed vehicle's value.
    /// </summary>
    public double SunlitFraction(string vehicleId)
    {
        ArgumentNullException.ThrowIfNull(vehicleId);
        return vehicleId == _flight.TargetId && _flight.Target is { } target ? SunlitFraction(target) : SunlitFraction(_flight.Crewed);
    }

    private double SunlitFraction(OrbitPropagator vehicle) =>
        SunlitFraction(vehicle.State.PositionM, vehicle.State.SpeedMps, vehicle.Body.RadiusM, _sunLine, Definition.PenumbraS);

    /// <summary>1 on the day side or outside the shadow cylinder, 0 inside it, linear across a penumbra of <paramref name="penumbraS"/> of flight.</summary>
    public static double SunlitFraction(Vec2d positionM, double speedMps, double bodyRadiusM, Vec2d sunLine, double penumbraS)
    {
        if (positionM.Dot(sunLine) >= 0) return 1.0;
        var fromAxisM = Math.Abs(positionM.Cross(sunLine));
        var halfWidthM = 0.5 * penumbraS * speedMps;
        if (!(halfWidthM > 0)) return fromAxisM >= bodyRadiusM ? 1.0 : 0.0;
        return Math.Clamp((fromAxisM - (bodyRadiusM - halfWidthM)) / (2 * halfWidthM), 0.0, 1.0);
    }
}
