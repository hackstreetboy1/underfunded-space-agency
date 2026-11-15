using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// Two-body orbit in the body's inertial plane, elliptic or hyperbolic. The true anomaly is measured in the
/// direction of motion, so a retrograde orbit (<see cref="Direction"/> = -1) sits at angle ω - ν.
/// </summary>
/// <param name="SemiMajorAxisM">Negative for hyperbolic orbits.</param>
/// <param name="Direction">+1 prograde (counter-clockwise), -1 retrograde.</param>
public readonly record struct Conic(
    double MuM3PerS2,
    double SemiMajorAxisM,
    double Eccentricity,
    double ArgPeriapsisRad,
    double MeanAnomalyAtEpochRad,
    MissionTime Epoch,
    int Direction)
{
    private const double CircularEccentricity = 1e-11;

    public static Conic FromState(in StateVector state, double muM3PerS2, MissionTime epoch)
    {
        var r = state.PositionM;
        var v = state.VelocityMps;
        var radius = r.Length;
        var speedSquared = v.LengthSquared;
        var eccentricityVector = ((speedSquared - muM3PerS2 / radius) * r - r.Dot(v) * v) / muM3PerS2;
        var eccentricity = eccentricityVector.Length;
        var semiMajorAxis = 1.0 / (2.0 / radius - speedSquared / muM3PerS2);
        var direction = state.OrbitSense;
        var argPeriapsis = eccentricity > CircularEccentricity ? eccentricityVector.Angle : 0.0;
        var trueAnomaly = direction * Angles.Wrap(r.Angle - argPeriapsis);
        return new Conic(muM3PerS2, semiMajorAxis, eccentricity, argPeriapsis,
            Kepler.MeanFromTrue(trueAnomaly, eccentricity), epoch, direction);
    }

    /// <summary>Builds an orbit from apsis radii and the true anomaly at <paramref name="epoch"/>.</summary>
    public static Conic FromApsides(
        double muM3PerS2, double periapsisM, double apoapsisM, double argPeriapsisRad, double trueAnomalyRad,
        MissionTime epoch, int direction = 1)
    {
        if (periapsisM <= 0 || apoapsisM < periapsisM)
            throw new ArgumentException($"Apsides must satisfy 0 < periapsis <= apoapsis (got {periapsisM}, {apoapsisM}).");
        var eccentricity = (apoapsisM - periapsisM) / (apoapsisM + periapsisM);
        return new Conic(muM3PerS2, (periapsisM + apoapsisM) / 2, eccentricity, argPeriapsisRad,
            Kepler.MeanFromTrue(trueAnomalyRad, eccentricity), epoch, direction >= 0 ? 1 : -1);
    }

    public bool IsHyperbolic => Eccentricity >= 1;

    public double SemiLatusRectumM => SemiMajorAxisM * (1 - Eccentricity * Eccentricity);

    public double MeanMotionRadPerS => Math.Sqrt(MuM3PerS2 / Math.Abs(SemiMajorAxisM * SemiMajorAxisM * SemiMajorAxisM));

    public double PeriodS => IsHyperbolic ? double.PositiveInfinity : Angles.TwoPi / MeanMotionRadPerS;

    public double SpecificEnergyJPerKg => -MuM3PerS2 / (2 * SemiMajorAxisM);

    public double PeriapsisM => SemiMajorAxisM * (1 - Eccentricity);

    public double ApoapsisM => IsHyperbolic ? double.PositiveInfinity : SemiMajorAxisM * (1 + Eccentricity);

    public double MeanAnomalyAt(MissionTime time) =>
        MeanAnomalyAtEpochRad + MeanMotionRadPerS * ((time.Milliseconds - Epoch.Milliseconds) / 1000.0);

    public double TrueAnomalyAt(MissionTime time) => Kepler.TrueFromMean(MeanAnomalyAt(time), Eccentricity);

    public StateVector StateAt(MissionTime time) => StateAtTrueAnomaly(TrueAnomalyAt(time));

    public StateVector StateAtTrueAnomaly(double trueAnomalyRad)
    {
        var p = SemiLatusRectumM;
        var cos = Math.Cos(trueAnomalyRad);
        var sin = Math.Sin(trueAnomalyRad);
        var radius = p / (1 + Eccentricity * cos);
        var angle = ArgPeriapsisRad + Direction * trueAnomalyRad;
        var radialUnit = Vec2d.FromPolar(1, angle);
        var speedScale = Math.Sqrt(MuM3PerS2 / p);
        var radialSpeed = speedScale * Eccentricity * sin;
        var tangentialSpeed = speedScale * (1 + Eccentricity * cos);
        return new StateVector(radius * radialUnit, radialSpeed * radialUnit + Direction * tangentialSpeed * radialUnit.Perpendicular);
    }

    /// <summary>The non-negative true anomaly at which the orbit reaches <paramref name="radiusM"/>, or NaN if it never does.</summary>
    public double TrueAnomalyAtRadius(double radiusM)
    {
        if (Eccentricity < CircularEccentricity) return double.NaN;
        var cos = (SemiLatusRectumM / radiusM - 1) / Eccentricity;
        return cos is < -1 or > 1 ? double.NaN : Math.Acos(cos);
    }

    public double FlightPathAngleAtTrueAnomaly(double trueAnomalyRad) =>
        Math.Atan2(Eccentricity * Math.Sin(trueAnomalyRad), 1 + Eccentricity * Math.Cos(trueAnomalyRad));

    /// <summary>First time at or after <paramref name="after"/> when the orbit passes <paramref name="trueAnomalyRad"/>.</summary>
    /// <returns>Null for a hyperbolic orbit that has already passed (or never reaches) that anomaly.</returns>
    public MissionTime? NextTimeAtTrueAnomaly(double trueAnomalyRad, MissionTime after)
    {
        var target = Kepler.MeanFromTrue(trueAnomalyRad, Eccentricity);
        if (double.IsNaN(target)) return null;
        double seconds;
        if (IsHyperbolic)
        {
            seconds = (target - MeanAnomalyAt(after)) / MeanMotionRadPerS;
            if (seconds < 0) return null;
        }
        else
        {
            seconds = Angles.WrapPositive(target - MeanAnomalyAt(after)) / MeanMotionRadPerS;
        }
        return new MissionTime(after.Milliseconds + (long)Math.Ceiling(seconds * 1000.0));
    }
}
