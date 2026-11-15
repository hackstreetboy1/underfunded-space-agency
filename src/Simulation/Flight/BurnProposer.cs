using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// Easy-mode navigation assist. Solves for burn duration (and, for zone targeting, start time) on
/// <see cref="ManeuverPlanner.Preview(OrbitPropagator, ManeuverPlan, ChuteState, ChuteState)"/>. It only proposes; executing is the player's decision. A pure function of its
/// arguments: it keeps no state, so separate calls may run on separate threads.
/// </summary>
/// <remarks>
/// Searches fly only as far as they need (the resulting conic, or the entry interface) and the answer is previewed in
/// full. Zone timing scans an analytic stand-in on the <c>zoneScanStepS</c> grid and refines on a few flown previews
/// (<see cref="ZoneTimingSolver"/>), falling back to scanning flown previews when the stand-in does not apply.
/// </remarks>
public sealed class BurnProposer(ManeuverPlanner planner)
{
    public const string KeyLowerPeriapsis = "nav.propose.lower_periapsis";
    public const string KeyRaiseOrbit = "nav.propose.raise_orbit";
    public const string KeyTargetZone = "nav.propose.target_zone";
    public const string KeyAlreadySatisfied = "nav.propose.already_satisfied";
    public const string KeyInsufficientDeltaV = "nav.propose.insufficient_delta_v";
    public const string KeyZoneUnreachable = "nav.propose.zone_unreachable";
    public const string KeyUnknownZone = "nav.propose.unknown_zone";
    public const string KeyUnavailable = "nav.propose.unavailable";

    private readonly ManeuverPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));

    /// <param name="current">Left untouched; previews fly clones of it.</param>
    /// <param name="uncertainty">What the ground knows of <paramref name="current"/> (X8): the proposal's preview then carries the landing dispersion.</param>
    public BurnProposal Propose(
        OrbitPropagator current, BurnGoal goal, BurnEngine engine, MissionTime earliestStart, PreviewUncertainty? uncertainty = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(engine);
        if (!ManeuverPlanner.CanPreview(current)) return Fail(goal, KeyUnavailable);
        var start = earliestStart < current.Time ? current.Time : earliestStart;
        // Every preview starts at or after `start`, so they share one coast up to it.
        var origin = ManeuverPlanner.CoastedTo(current, start);

        return goal switch
        {
            LowerPeriapsisTo lower => SolveForDuration(origin, goal, engine, BurnDirection.Retrograde, start, KeyLowerPeriapsis, uncertainty,
                preview => preview.PeriapsisKm - lower.PeriapsisKm),
            RaiseOrbitBy raise => SolveForDuration(origin, goal, engine, BurnDirection.Prograde, start, KeyRaiseOrbit, uncertainty,
                RaiseError(current, raise.DeltaKm)),
            TargetZone zone => SolveForZone(current, origin, zone, engine, start, uncertainty),
            _ => throw new ArgumentOutOfRangeException(nameof(goal), goal, "Unknown burn goal."),
        };
    }

    private static Func<ManeuverPreview, double> RaiseError(OrbitPropagator current, double deltaKm)
    {
        var initialSemiMajorAxisM = current.Conic.SemiMajorAxisM;
        return preview => deltaKm - (preview.ResultingConic.SemiMajorAxisM - initialSemiMajorAxisM) / 1000;
    }

    /// <summary>Finds the shortest duration (in 0.1 s steps) at which the decreasing <paramref name="error"/> reaches zero.</summary>
    /// <param name="error">Reads only the resulting conic, so the search flies no entries.</param>
    private BurnProposal SolveForDuration(
        OrbitPropagator origin, BurnGoal goal, BurnEngine engine, BurnDirection direction, MissionTime start,
        string successKey, PreviewUncertainty? uncertainty, Func<ManeuverPreview, double> error)
    {
        ManeuverPlan PlanSteps(long steps) =>
            new(engine, direction, steps * OrbitPropagator.IntegrationStepMs / 1000.0, start);
        double ErrorAt(long steps) => error(_planner.Fly(origin, PlanSteps(steps), PreviewExtent.Burn, out _));

        var zeroError = ErrorAt(0);
        if (zeroError <= 0) return Fail(goal, KeyAlreadySatisfied);

        var maxSteps = MaxDurationSteps(engine, origin.MassKg);
        var ladder = new BurnLadder(_planner, origin, PlanSteps, maxSteps);
        double LadderError(long steps) => error(ladder.Fly(steps, PreviewExtent.Burn));
        var full = ladder.Fly(maxSteps, PreviewExtent.Burn);
        var fullError = error(full);
        if (fullError > 0) return Fail(goal, KeyInsufficientDeltaV, new ExplanationArgument("delta_v_mps", full.DeltaVMps));
        var steps = engine.FixedDurationS is null ? FirstNonPositive(LadderError, 0, zeroError, maxSteps, fullError) : maxSteps;
        return Success(goal, _planner.Preview(origin, PlanSteps(steps), uncertainty), successKey);
    }

    private BurnProposal SolveForZone(
        OrbitPropagator current, OrbitPropagator origin, TargetZone goal, BurnEngine engine, MissionTime earliest, PreviewUncertainty? uncertainty)
    {
        var mission = _planner.Mission;
        var zone = mission.Zones.Find(goal.ZoneId);
        if (zone is null) return Fail(goal, KeyUnknownZone);

        var durationS = engine.FixedDurationS ?? SolveCorridorDuration(origin, engine, earliest);
        if (durationS is null) return Fail(goal, KeyInsufficientDeltaV);

        var planning = mission.Planning;
        var grid = new ZoneSearchGrid(
            earliest,
            (long)(planning.ZoneScanStepS * 1000),
            (long)(planning.ZoneSearchWindowOrbits * current.Conic.PeriodS * 1000),
            Math.Max(1, (long)(planning.StartTimeToleranceS * 1000)));
        var timing = new ZoneTimingSolver(_planner, origin, zone, engine, durationS.Value, grid).Solve();
        return timing.Status switch
        {
            ZoneTimingStatus.Found => timing.Preview!.LandingZone?.ZoneId == zone.Id
                ? Success(goal, Dispersed(origin, timing.Preview, timing.DroguePoint, uncertainty), KeyTargetZone)
                : Fail(goal, KeyZoneUnreachable),
            ZoneTimingStatus.Unreachable => Fail(goal, KeyZoneUnreachable),
            _ => ScanForZone(origin, goal, zone, engine, durationS.Value, grid, uncertainty),
        };
    }

    /// <summary>
    /// The exhaustive zone search: full previews every grid step for the first crossing of the zone centre, then
    /// bisection to the start-time tolerance. Used when <see cref="ZoneTimingSolver"/> does not apply.
    /// </summary>
    private BurnProposal ScanForZone(
        OrbitPropagator origin, TargetZone goal, RecoveryZone zone, BurnEngine engine, double durationS, ZoneSearchGrid grid,
        PreviewUncertainty? uncertainty)
    {
        ManeuverPreview PreviewAt(MissionTime start) =>
            _planner.Preview(origin, new ManeuverPlan(engine, BurnDirection.Retrograde, durationS, start));

        double ZoneError(ManeuverPreview preview) =>
            preview.LandingZone is { } landing
                ? Angles.Wrap(landing.LandingLongitudeDeg * Angles.DegToRad - zone.CentreLongitudeRad)
                : double.NaN;

        var previousTime = grid.Earliest;
        var previousError = ZoneError(PreviewAt(grid.Earliest));
        for (var index = 1L; index <= grid.LastIndex; index++)
        {
            var time = grid.At(index);
            var timeError = ZoneError(PreviewAt(time));
            // A later start moves the landing east on a prograde orbit and west on a retrograde one, so a zero
            // crossing counts in either direction; a jump of π/2 or more is the ±π wrap, not a crossing.
            if ((previousError < 0) != (timeError < 0) && Math.Abs(timeError - previousError) < Math.PI / 2)
            {
                var refined = RefineZoneStart(goal, zone, previousTime, time, rising: previousError < 0, grid.ToleranceMs, PreviewAt, ZoneError);
                return refined.Preview is { } best && uncertainty is not null
                    ? Success(goal, Dispersed(origin, best, null, uncertainty), KeyTargetZone)
                    : refined;
            }
            previousTime = time;
            previousError = timeError;
        }
        return Fail(goal, KeyZoneUnreachable);
    }

    private static BurnProposal RefineZoneStart(
        TargetZone goal, RecoveryZone zone, MissionTime lo, MissionTime hi, bool rising, long toleranceMs,
        Func<MissionTime, ManeuverPreview> previewAt, Func<ManeuverPreview, double> zoneError)
    {
        var best = previewAt(hi);
        while (hi.Milliseconds - lo.Milliseconds > toleranceMs)
        {
            var mid = new MissionTime(lo.Milliseconds + (hi.Milliseconds - lo.Milliseconds) / 2);
            var preview = previewAt(mid);
            var error = zoneError(preview);
            if (rising ? error >= 0 : error < 0)
            {
                hi = mid;
                best = preview;
            }
            else
            {
                lo = mid;
            }
        }
        return best.LandingZone?.ZoneId == zone.Id ? Success(goal, best, KeyTargetZone) : Fail(goal, KeyZoneUnreachable);
    }

    /// <summary>Duration putting the entry flight-path angle at the corridor centre when burning at <paramref name="start"/>.</summary>
    private double? SolveCorridorDuration(OrbitPropagator origin, BurnEngine engine, MissionTime start)
    {
        var target = _planner.Mission.Corridor.CentreFlightPathAngleDeg;
        ManeuverPlan PlanSteps(long steps) =>
            new(engine, BurnDirection.Retrograde, steps * OrbitPropagator.IntegrationStepMs / 1000.0, start);
        var maxSteps = MaxDurationSteps(engine, origin.MassKg);
        var ladder = new BurnLadder(_planner, origin, PlanSteps, maxSteps);
        double Error(long steps) => (ladder.Fly(steps, PreviewExtent.Interface).Entry?.FlightPathAngleDeg ?? 0) - target;

        var maxError = Error(maxSteps);
        if (maxError > 0) return null;
        var zeroError = Error(0);
        var steps = zeroError <= 0 ? 1 : FirstNonPositive(Error, 0, zeroError, maxSteps, maxError);
        return steps * OrbitPropagator.IntegrationStepMs / 1000.0;
    }

    /// <summary>
    /// The smallest n in (<paramref name="lo"/>, <paramref name="hi"/>] with f(n) &lt;= 0 for a non-increasing f with
    /// f(lo) &gt; 0 &gt;= f(hi): what bisection finds, from fewer evaluations. Secant steps aim at the root's ceiling, and
    /// a step that fails to halve the bracket is followed by a bisection, so it never needs more than about twice
    /// bisection's count.
    /// </summary>
    private static long FirstNonPositive(Func<long, double> f, long lo, double fLo, long hi, double fHi)
    {
        var bisect = false;
        while (hi - lo > 1)
        {
            var width = hi - lo;
            var span = fLo - fHi;
            var mid = bisect || !(span > 0) || !double.IsFinite(span)
                ? lo + width / 2
                : Math.Clamp((long)Math.Ceiling(lo + width * (fLo / span)), lo + 1, hi - 1);
            var value = f(mid);
            if (value <= 0)
            {
                hi = mid;
                fHi = value;
            }
            else
            {
                lo = mid;
                fLo = value;
            }
            bisect = !bisect && (hi - lo) * 2 > width;
        }
        return hi;
    }

    private long MaxDurationSteps(BurnEngine engine, double massKg)
    {
        var seconds = engine.FixedDurationS ?? _planner.Mission.Planning.MaxBurnDurationS;
        var level = engine.Sample(0);
        if (engine.FixedDurationS is null && level.MassFlowKgPerS > 0)
            seconds = Math.Min(seconds, Math.Min(engine.AvailablePropellantKg, massKg) / level.MassFlowKgPerS);
        return Math.Max(1, (long)Math.Ceiling(seconds * 1000 / OrbitPropagator.IntegrationStepMs));
    }

    private ManeuverPreview Dispersed(OrbitPropagator origin, ManeuverPreview preview, TimedState? droguePoint, PreviewUncertainty? uncertainty) =>
        uncertainty is null ? preview : _planner.Disperse(origin, preview, droguePoint, uncertainty);

    private static BurnProposal Success(BurnGoal goal, ManeuverPreview preview, string key)
    {
        var arguments = new List<ExplanationArgument>
        {
            new("start_met_s", preview.Plan.StartTime.Seconds),
            new("duration_s", preview.Plan.EffectiveDurationS),
            new("delta_v_mps", preview.DeltaVMps),
            new("propellant_kg", preview.PropellantUsedKg),
            new("periapsis_km", preview.PeriapsisKm),
            new("apoapsis_km", preview.ApoapsisKm),
        };
        if (preview.Entry is { } entry) arguments.Add(new("entry_angle_deg", entry.FlightPathAngleDeg));
        if (preview.LandingZone is { } zone)
        {
            arguments.Add(new("landing_longitude_deg", zone.LandingLongitudeDeg));
            arguments.Add(new("recovery_wait_h", zone.RecoveryWaitHours));
        }
        if (preview.Dispersion is { } dispersion) arguments.Add(new("landing_uncertainty_km", dispersion.HalfWidthKm));
        return new BurnProposal(goal, preview.Plan, preview, key, arguments);
    }

    private static BurnProposal Fail(BurnGoal goal, string key, params ExplanationArgument[] arguments) =>
        new(goal, null, null, key, arguments);
}
