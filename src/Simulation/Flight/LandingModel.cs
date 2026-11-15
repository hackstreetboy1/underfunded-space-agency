using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// Stand-in for where a retrofire comes down as a function of its start time, for <see cref="ZoneTimingSolver"/>. A
/// reference burn is flown once; its effect, as a departure from the unburned coast in the frame of the velocity, is
/// carried to any other start (exact on a circular orbit, close on a near-circular one). From there a Kepler coast gives
/// the entry interface in microseconds (<see cref="InterfaceLongitudeRad"/>), and a coarse integration of the atmosphere
/// down to the drogue altitude (<see cref="DescentLongitudeRad"/>), tens of microseconds, follows how the entry angle
/// changes the downrange. Neither is a preview: the solver calibrates them on flown ones.
/// </summary>
internal readonly struct LandingModel
{
    /// <summary>The coarse descent's step: its error is far below the transport's at up to a few seconds.</summary>
    private const double DescentStepS = 2;

    /// <summary>Far longer than any entry from the interface to the drogue altitude; a skip-out never gets there.</summary>
    private const int MaxDescentSteps = 5000;

    private readonly Conic _coast;
    private readonly Vec2d _positionDepartureM;
    private readonly Vec2d _velocityDepartureMps;
    private readonly long _burnMs;
    private readonly CelestialBody _body;
    private readonly double _interfaceRadiusM;
    private readonly double _ballisticCoefficientKgPerM2;
    private readonly double _stopRadiusM;
    private readonly RecoveryZones _zones;

    private LandingModel(
        Conic coast, in PreviewTrace reference, CelestialBody body, double ballisticCoefficientKgPerM2, double stopAltitudeM, RecoveryZones zones)
    {
        _coast = coast;
        _burnMs = reference.PostBurn.Time.Milliseconds - reference.PreBurn.Time.Milliseconds;
        var unburned = coast.StateAt(reference.PostBurn.Time);
        var toLocal = -unburned.VelocityMps.Angle;
        _positionDepartureM = (reference.PostBurn.State.PositionM - unburned.PositionM).Rotated(toLocal);
        _velocityDepartureMps = (reference.PostBurn.State.VelocityMps - unburned.VelocityMps).Rotated(toLocal);
        _body = body;
        _interfaceRadiusM = body.RadiusM + body.InterfaceAltitudeM;
        _ballisticCoefficientKgPerM2 = ballisticCoefficientKgPerM2;
        _stopRadiusM = body.RadiusM + stopAltitudeM;
        _zones = zones;
    }

    /// <param name="coast">The coast the burns start from (any epoch).</param>
    /// <param name="reference">A burn flown from that coast; it must end above the atmosphere.</param>
    /// <param name="ballisticCoefficientKgPerM2">The vehicle's, for the descent.</param>
    /// <param name="stopAltitudeM">Where <see cref="DescentLongitudeRad"/> stops (the drogue's nominal altitude).</param>
    public static bool TryCreate(
        Conic coast, in PreviewTrace reference, CelestialBody body, double ballisticCoefficientKgPerM2, double stopAltitudeM,
        RecoveryZones zones, out LandingModel model)
    {
        model = default;
        if (!reference.Burned || coast.IsHyperbolic || body.Atmosphere is null
            || reference.PostBurn.State.RadiusM <= body.RadiusM + body.InterfaceAltitudeM) return false;
        model = new LandingModel(coast, reference, body, ballisticCoefficientKgPerM2, stopAltitudeM, zones);
        return true;
    }

    /// <summary>Longitude below the entry interface crossing for a burn starting at <paramref name="startMs"/>; NaN if it never enters.</summary>
    public double InterfaceLongitudeRad(long startMs) =>
        TryEnter(startMs, out var interfaceS, out var state) ? Longitude(state.PositionM.Angle, interfaceS) : double.NaN;

    /// <summary>Longitude at the drogue altitude after a coarse descent through the atmosphere; NaN if it never gets there.</summary>
    public double DescentLongitudeRad(long startMs)
    {
        if (!TryEnter(startMs, out var timeS, out var state)) return double.NaN;
        var massKg = 1.0;
        for (var i = 0; i < MaxDescentSteps; i++)
        {
            var previous = state;
            FlightDynamics.Rk4Step(_body, _ballisticCoefficientKgPerM2, ThrustSample.None, DescentStepS, ref state, ref massKg);
            if (state.RadiusM <= _stopRadiusM)
            {
                var fraction = (previous.RadiusM - _stopRadiusM) / (previous.RadiusM - state.RadiusM);
                var angle = previous.PositionM.Angle + fraction * Angles.Wrap(state.PositionM.Angle - previous.PositionM.Angle);
                return Longitude(angle, timeS + fraction * DescentStepS);
            }
            timeS += DescentStepS;
        }
        return double.NaN;
    }

    /// <summary>The state where the carried-over burn's coast descends through the interface, and when (MET seconds).</summary>
    private bool TryEnter(long startMs, out double interfaceS, out StateVector interfaceState)
    {
        interfaceS = 0;
        interfaceState = default;
        var endMs = startMs + _burnMs;
        var unburned = _coast.StateAt(new MissionTime(endMs));
        var toInertial = unburned.VelocityMps.Angle;
        var after = new StateVector(
            unburned.PositionM + _positionDepartureM.Rotated(toInertial), unburned.VelocityMps + _velocityDepartureMps.Rotated(toInertial));
        if (after.RadiusM <= _interfaceRadiusM) return false;

        var conic = Conic.FromState(after, _coast.MuM3PerS2, new MissionTime(endMs));
        if (conic.IsHyperbolic) return false;
        var anomaly = conic.TrueAnomalyAtRadius(_interfaceRadiusM);
        if (double.IsNaN(anomaly)) return false;
        // The descending crossing is at true anomaly -ν; the mean anomaly gives the coast time to it.
        interfaceS = endMs / 1000.0
            + Angles.WrapPositive(Kepler.MeanFromTrue(-anomaly, conic.Eccentricity) - conic.MeanAnomalyAtEpochRad) / conic.MeanMotionRadPerS;
        interfaceState = conic.StateAtTrueAnomaly(-anomaly);
        return true;
    }

    private double Longitude(double inertialAngleRad, double metS) =>
        Angles.Wrap(inertialAngleRad - _zones.RotationAngleAtMetZeroRad - _zones.RotationRateRadPerS * metS);
}
