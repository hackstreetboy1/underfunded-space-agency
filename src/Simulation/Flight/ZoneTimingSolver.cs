using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>The start times the exhaustive zone search samples: every step from the earliest start across the window.</summary>
internal readonly record struct ZoneSearchGrid(MissionTime Earliest, long StepMs, long WindowMs, long ToleranceMs)
{
    /// <summary>Index of the last sampled start (the first is 0, the earliest start itself).</summary>
    public long LastIndex => WindowMs / StepMs;

    public MissionTime At(long index) => Earliest + index * StepMs;
}

internal enum ZoneTimingStatus
{
    Found,

    /// <summary>The exhaustive search would see no crossing of the zone centre in its window.</summary>
    Unreachable,

    /// <summary>The trajectory doesn't suit the stand-in (no entry from a coast on this body, no convergence): scan instead.</summary>
    Unsupported,
}

internal readonly record struct ZoneTiming(ZoneTimingStatus Status, ManeuverPreview? Preview = null, TimedState? DroguePoint = null);

/// <summary>
/// Times a fixed retrograde burn so the capsule lands on a recovery zone's centre with one or two flown previews instead
/// of a scan of a hundred. <see cref="LandingModel"/> stands in for the previews: its interface longitude scans the
/// window on the exhaustive search's grid in microseconds, keeping that search's rules (the first crossing of the centre
/// on the grid, inside the window) so both choose the same pass; its coarse descent then pins the crossing down, and a
/// full preview there confirms it within the start-time tolerance, or recalibrates the descent and tries again.
/// </summary>
internal sealed class ZoneTimingSolver
{
    private const int MaxFullPreviews = 3;

    /// <summary>
    /// Within this of the centre at the earliest start (far above the descent's error), a full preview settles which
    /// side the earliest start lands on, and so whether this pass counts.
    /// </summary>
    private const double EdgeMarginRad = 0.1 * Angles.DegToRad;

    /// <summary>Crossings the interface longitude puts this many steps past the window are still examined.</summary>
    private const long EndMarginSteps = 3;

    private readonly ManeuverPlanner _planner;
    private readonly OrbitPropagator _origin;
    private readonly RecoveryZone _zone;
    private readonly BurnEngine _engine;
    private readonly double _durationS;
    private readonly ZoneSearchGrid _grid;
    private readonly RecoveryZones _zones;
    private LandingModel _model;
    private double _interfaceOffsetRad;
    private double _descentOffsetRad;
    private TimedState? _lastDroguePoint;

    /// <param name="origin">The vehicle, coasting at or before the earliest start.</param>
    public ZoneTimingSolver(
        ManeuverPlanner planner, OrbitPropagator origin, RecoveryZone zone, BurnEngine engine, double durationS, ZoneSearchGrid grid)
    {
        _planner = planner;
        _origin = origin;
        _zone = zone;
        _engine = engine;
        _durationS = durationS;
        _grid = grid;
        _zones = planner.Mission.Zones;
    }

    /// <summary>Previews flown so far (any extent); the budget tests read it.</summary>
    public int PreviewsFlown { get; private set; }

    public ZoneTiming Solve()
    {
        var unsupported = new ZoneTiming(ZoneTimingStatus.Unsupported);
        var body = _origin.Body;
        if (_origin.Phase != FlightPhase.Coast || body.Atmosphere is null || _zones.BodyId != body.Id) return unsupported;

        PreviewsFlown++;
        _planner.Fly(_origin, PlanAt(_grid.Earliest), PreviewExtent.Burn, out var reference);
        if (!LandingModel.TryCreate(_origin.Conic, reference, body, _origin.BallisticCoefficientKgPerM2,
                _planner.Mission.Capsule.Drogue.NominalDeployAltitudeM, _zones, out _model)) return unsupported;

        var earliestMs = _grid.Earliest.Milliseconds;
        var earliestError = DescentError(earliestMs);
        _interfaceOffsetRad = Angles.Wrap(_model.DescentLongitudeRad(earliestMs) - _model.InterfaceLongitudeRad(earliestMs));
        if (!double.IsFinite(earliestError) || !double.IsFinite(_interfaceOffsetRad)) return unsupported;
        if (Math.Abs(earliestError) < EdgeMarginRad)
        {
            if (Fly(earliestMs).LandingZone is not { } landing) return unsupported;
            earliestError = CentreError(landing);
        }

        var crossing = FirstCrossing(earliestError, out var rising);
        if (crossing == 0) return unsupported;
        if (crossing < 0) return new ZoneTiming(ZoneTimingStatus.Unreachable);

        var startMs = RootNear(InterfaceRoot(_grid.At(crossing - 1).Milliseconds, _grid.At(crossing).Milliseconds));
        for (var i = 0; i < MaxFullPreviews; i++)
        {
            if (startMs <= earliestMs) return unsupported;
            var preview = Fly(startMs);
            if (preview.LandingZone is not { } landing) return unsupported;
            var error = CentreError(landing);
            if (Math.Abs(error) <= Math.Abs(SlopePerMs(startMs)) * _grid.ToleranceMs) return InsideWindow(startMs, rising, preview);
            _descentOffsetRad = Angles.Wrap(_descentOffsetRad + error - DescentError(startMs));
            startMs = RootNear(startMs);
        }
        return unsupported;
    }

    /// <summary>
    /// The first grid step at which the interface longitude crosses the centre, by the exhaustive search's rule (a sign
    /// change that isn't the ±π wrap); -1 when there is none up to a little past the window, 0 when the model breaks down.
    /// </summary>
    private long FirstCrossing(double earliestError, out bool rising)
    {
        rising = false;
        var previous = earliestError;
        for (var index = 1L; index <= _grid.LastIndex + EndMarginSteps; index++)
        {
            var error = InterfaceError(_grid.At(index).Milliseconds);
            if (double.IsNaN(error)) return 0;
            if ((previous < 0) != (error < 0) && Math.Abs(error - previous) < Math.PI / 2)
            {
                rising = previous < 0;
                return index;
            }
            previous = error;
        }
        return -1;
    }

    /// <summary>The exhaustive search sees a crossing only when its last sample lies past it; near that sample, fly it to see.</summary>
    private ZoneTiming InsideWindow(long startMs, bool rising, ManeuverPreview found)
    {
        var lastMs = _grid.At(_grid.LastIndex).Milliseconds;
        var marginMs = 2 * _grid.ToleranceMs;
        if (startMs > lastMs + marginMs) return new ZoneTiming(ZoneTimingStatus.Unreachable);
        if (startMs >= lastMs - marginMs)
        {
            if (Fly(lastMs).LandingZone is not { } landing) return new ZoneTiming(ZoneTimingStatus.Unsupported);
            var error = CentreError(landing);
            if (rising ? error < 0 : error >= 0) return new ZoneTiming(ZoneTimingStatus.Unreachable);
        }
        return new ZoneTiming(ZoneTimingStatus.Found, found, _lastDroguePoint);
    }

    /// <summary>Bisects the interface longitude to the millisecond inside a bracket (an end if it has no sign change there).</summary>
    private long InterfaceRoot(long loMs, long hiMs)
    {
        var loError = InterfaceError(loMs);
        var hiError = InterfaceError(hiMs);
        if ((loError < 0) == (hiError < 0)) return Math.Abs(loError) < Math.Abs(hiError) ? loMs : hiMs;
        while (hiMs - loMs > 1)
        {
            var midMs = loMs + (hiMs - loMs) / 2;
            var midError = InterfaceError(midMs);
            if ((midError < 0) == (loError < 0))
            {
                loMs = midMs;
                loError = midError;
            }
            else
            {
                hiMs = midMs;
            }
        }
        return hiMs;
    }

    /// <summary>Newton steps on the descent longitude, with the interface longitude's slope, each at most one grid step.</summary>
    private long RootNear(long startMs)
    {
        double timeMs = startMs;
        for (var i = 0; i < 8; i++)
        {
            var rounded = (long)Math.Round(timeMs);
            var error = DescentError(rounded);
            var slope = SlopePerMs(rounded);
            if (!double.IsFinite(error) || !double.IsFinite(slope) || slope == 0) break;
            var stepMs = Math.Clamp(-error / slope, -_grid.StepMs, _grid.StepMs);
            timeMs += stepMs;
            if (Math.Abs(stepMs) < 1) break;
        }
        return (long)Math.Round(timeMs);
    }

    private double InterfaceError(long startMs) =>
        Angles.Wrap(_model.InterfaceLongitudeRad(startMs) + _interfaceOffsetRad - _zone.CentreLongitudeRad);

    private double DescentError(long startMs) =>
        Angles.Wrap(_model.DescentLongitudeRad(startMs) + _descentOffsetRad - _zone.CentreLongitudeRad);

    private double CentreError(ZoneEvaluation landing) =>
        Angles.Wrap(landing.LandingLongitudeDeg * Angles.DegToRad - _zone.CentreLongitudeRad);

    private double SlopePerMs(long startMs)
    {
        const long HalfSpanMs = 1000;
        return Angles.Wrap(_model.InterfaceLongitudeRad(startMs + HalfSpanMs) - _model.InterfaceLongitudeRad(startMs - HalfSpanMs))
            / (2 * HalfSpanMs);
    }

    private ManeuverPreview Fly(long startMs)
    {
        PreviewsFlown++;
        var preview = _planner.Fly(_origin, PlanAt(new MissionTime(startMs)), PreviewExtent.Landing, out var trace);
        _lastDroguePoint = trace.DroguePoint;
        return preview;
    }

    private ManeuverPlan PlanAt(MissionTime start) => new(_engine, BurnDirection.Retrograde, _durationS, start);
}
