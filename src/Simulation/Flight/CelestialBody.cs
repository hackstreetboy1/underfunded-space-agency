using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>A gravitating body. Moons move on circular orbits about their parent (patched conics).</summary>
public sealed class CelestialBody
{
    private CelestialBody[] _children = [];

    internal CelestialBody(BodyDefinition definition, CelestialBody? parent)
    {
        Id = definition.Id;
        MuM3PerS2 = definition.MuM3PerS2;
        RadiusM = definition.RadiusM;
        SoiRadiusM = definition.SoiRadiusM ?? double.PositiveInfinity;
        RotationRateRadPerS = definition.RotationRateRadPerS;
        Atmosphere = definition.Atmosphere;
        Parent = parent;
        if (parent is not null && definition.Orbit is { } orbit)
        {
            OrbitRadiusM = orbit.RadiusM;
            OrbitPhaseAtMetZeroRad = orbit.PhaseAtMetZeroDeg * Angles.DegToRad;
            OrbitSense = orbit.Direction == OrbitDirection.Prograde ? 1 : -1;
            OrbitRateRadPerS = Math.Sqrt(parent.MuM3PerS2 / (orbit.RadiusM * orbit.RadiusM * orbit.RadiusM));
        }
    }

    public string Id { get; }

    public double MuM3PerS2 { get; }

    public double RadiusM { get; }

    /// <summary>Infinite for the root body.</summary>
    public double SoiRadiusM { get; }

    public double RotationRateRadPerS { get; }

    public AtmosphereModel? Atmosphere { get; }

    public CelestialBody? Parent { get; }

    /// <summary>Bodies orbiting this one, sorted by id.</summary>
    public IReadOnlyList<CelestialBody> Children => _children;

    public double OrbitRadiusM { get; }

    public double OrbitPhaseAtMetZeroRad { get; }

    /// <summary>+1 when the body orbits its parent counter-clockwise, -1 otherwise.</summary>
    public int OrbitSense { get; }

    public double OrbitRateRadPerS { get; }

    /// <summary>Altitude at which drag starts and the atmospheric segment begins; zero for airless bodies.</summary>
    public double InterfaceAltitudeM => Atmosphere?.InterfaceAltitudeM ?? 0;

    internal CelestialBody[] ChildArray => _children;

    public Vec2d PositionInParentAt(MissionTime time) =>
        Parent is null ? Vec2d.Zero : Vec2d.FromPolar(OrbitRadiusM, OrbitAngleAt(time));

    public Vec2d VelocityInParentAt(MissionTime time) =>
        Parent is null
            ? Vec2d.Zero
            : Vec2d.FromPolar(OrbitRadiusM * OrbitRateRadPerS, OrbitAngleAt(time)).Perpendicular * OrbitSense;

    /// <summary>Velocity of the co-rotating atmosphere (or surface) at <paramref name="positionM"/>.</summary>
    public Vec2d SurfaceVelocityAt(Vec2d positionM) => positionM.Perpendicular * RotationRateRadPerS;

    /// <summary>Velocity relative to the co-rotating atmosphere at the state's position.</summary>
    public Vec2d AirVelocityOf(in StateVector state) => state.VelocityMps - SurfaceVelocityAt(state.PositionM);

    internal void SetChildren(CelestialBody[] children) => _children = children;

    private double OrbitAngleAt(MissionTime time) =>
        OrbitPhaseAtMetZeroRad + OrbitSense * OrbitRateRadPerS * time.Seconds;
}
