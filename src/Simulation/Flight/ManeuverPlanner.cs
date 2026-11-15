using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// Previews planned burns on a clone of the flight state, using the same integrators and step grid as the real
/// flight, so an undisturbed burn lands exactly where the preview said. The entry is flown with the chutes opened
/// at their nominal altitudes (the entry checklist), except a chute that is already deployed or has failed.
/// </summary>
public sealed class ManeuverPlanner(MissionFlight mission)
{
    /// <summary>Leftover propellant below this is summation noise, not usable Δv.</summary>
    private const double NegligiblePropellantKg = 1e-6;

    private readonly MissionFlight _mission = mission ?? throw new ArgumentNullException(nameof(mission));

    public MissionFlight Mission => _mission;

    public static bool CanPreview(OrbitPropagator current) =>
        current is not null && current.Phase is FlightPhase.Coast or FlightPhase.Atmospheric;

    /// <summary>Pure function of <paramref name="current"/> (left untouched), <paramref name="plan"/> and the chute states.</summary>
    /// <param name="drogue">The real drogue state: a deployed or failed chute is not deployed again during the entry.</param>
    /// <param name="main">The real main chute state, as for <paramref name="drogue"/>.</param>
    /// <exception cref="InvalidOperationException">The vehicle is mid-burn or has landed.</exception>
    public ManeuverPreview Preview(
        OrbitPropagator current, ManeuverPlan plan, ChuteState drogue = ChuteState.Stowed, ChuteState main = ChuteState.Stowed) =>
        Fly(current, plan, PreviewExtent.Landing, out _, drogue, main);

    /// <summary>
    /// <see cref="Preview(OrbitPropagator, ManeuverPlan, ChuteState, ChuteState)"/> on the ground's estimate, with the
    /// landing's <see cref="LandingDispersion"/> from <paramref name="uncertainty"/> (X8): two more previews flown to
    /// the drogue altitude, one from a start 1σ higher and one at the far edge of the mass band; along-track error moves
    /// the landing by its own length. The three add in quadrature.
    /// </summary>
    public ManeuverPreview Preview(
        OrbitPropagator current, ManeuverPlan plan, PreviewUncertainty? uncertainty,
        ChuteState drogue = ChuteState.Stowed, ChuteState main = ChuteState.Stowed)
    {
        var preview = Fly(current, plan, PreviewExtent.Landing, out var trace, drogue, main);
        return uncertainty is null ? preview : Disperse(current, preview, trace.DroguePoint, uncertainty, drogue, main);
    }

    /// <summary>Adds the dispersion to a landing <paramref name="preview"/> flown from <paramref name="current"/>.</summary>
    /// <param name="droguePoint">The preview's own drogue point if known; flown again otherwise.</param>
    internal ManeuverPreview Disperse(
        OrbitPropagator current, ManeuverPreview preview, TimedState? droguePoint, PreviewUncertainty uncertainty,
        ChuteState drogue = ChuteState.Stowed, ChuteState main = ChuteState.Stowed)
    {
        uncertainty.RequireValid();
        var zones = _mission.Zones;
        if (preview.LandingZone is not { } landing || zones.BodyId != current.Body.Id) return preview;
        double DrogueLongitude(OrbitPropagator from)
        {
            Fly(from, preview.Plan, PreviewExtent.Drogue, out var trace, drogue, main);
            return trace.DroguePoint is { } point ? zones.LandingLongitudeRad(point.State.PositionM.Angle, point.Time) : double.NaN;
        }

        var nominal = droguePoint is { } known
            ? zones.LandingLongitudeRad(known.State.PositionM.Angle, known.Time)
            : DrogueLongitude(current);

        var state = current.State;
        var radialShift = uncertainty.SigmaRadialM > 0
            ? Angles.Wrap(DrogueLongitude(current.Displaced(state with { PositionM = state.PositionM + state.PositionM.Normalized() * uncertainty.SigmaRadialM })) - nominal)
            : 0;

        var massShift = 0.0;
        var massKg = current.MassKg;
        var edgeKg = uncertainty.MassHighKg - massKg >= massKg - uncertainty.MassLowKg ? uncertainty.MassHighKg : uncertainty.MassLowKg;
        if (uncertainty.MassHighKg > uncertainty.MassLowKg && edgeKg != massKg)
        {
            var edge = current.Clone();
            edge.SetMass(edgeKg);
            var sigmaKg = (uncertainty.MassHighKg - uncertainty.MassLowKg) / (2 * uncertainty.DisplaySigma);
            massShift = Angles.Wrap(DrogueLongitude(edge) - nominal) * sigmaKg / Math.Abs(edgeKg - massKg);
        }
        var alongShift = uncertainty.SigmaAlongM / state.RadiusM;
        var sigmaRad = Math.Sqrt(radialShift * radialShift + massShift * massShift + alongShift * alongShift);
        if (!double.IsFinite(sigmaRad)) return preview;

        var halfWidthRad = Math.Min(Math.PI, uncertainty.DisplaySigma * sigmaRad);
        var centre = landing.LandingLongitudeDeg * Angles.DegToRad;
        var ve = preview.Plan.Engine.ExhaustVelocityMps;
        var used = preview.PropellantUsedKg;
        double DeltaV(double kg) => used > 0 && kg > used && ve > 0 ? ve * Math.Log(kg / (kg - used)) : preview.DeltaVMps;
        var zone = landing.ZoneId is { } zoneId ? zones.Find(zoneId) : null;
        return preview with
        {
            Dispersion = new LandingDispersion(
                halfWidthRad, halfWidthRad * current.Body.RadiusM / 1000, uncertainty.DisplaySigma,
                DeltaV(uncertainty.MassHighKg), DeltaV(uncertainty.MassLowKg),
                zones.WorstWaitHours(centre, halfWidthRad),
                zone is not null && zone.ContainsArc(centre, halfWidthRad)),
        };
    }

    /// <summary>
    /// A clone of <paramref name="current"/> coasted to <paramref name="time"/>, so previews starting then or later share
    /// that coast and still match previews from <paramref name="current"/> exactly (a coast is analytic, so where it
    /// pauses changes nothing); <paramref name="current"/> itself when it is not coasting or an event lies on the way.
    /// </summary>
    internal static OrbitPropagator CoastedTo(OrbitPropagator current, MissionTime time)
    {
        if (current.Phase != FlightPhase.Coast || time <= current.Time) return current;
        var clone = current.Clone();
        return clone.AdvanceToNextTransition(time) || clone.Phase != FlightPhase.Coast || clone.Time != time ? current : clone;
    }

    /// <summary>
    /// <see cref="Preview(OrbitPropagator, ManeuverPlan, ChuteState, ChuteState)"/> that may stop early: after the burn, at the entry interface, or at the drogue's nominal
    /// altitude (below which a capsule drifts with the air). An early stop has no landing (and no entry for
    /// <see cref="PreviewExtent.Burn"/>); <paramref name="trace"/> holds the states either side of the burn and at the stop.
    /// </summary>
    internal ManeuverPreview Fly(
        OrbitPropagator current, ManeuverPlan plan, PreviewExtent extent, out PreviewTrace trace,
        ChuteState drogue = ChuteState.Stowed, ChuteState main = ChuteState.Stowed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(plan);
        plan.RequireFinite();
        if (!CanPreview(current)) throw new InvalidOperationException($"Cannot preview a burn while {current.Phase}.");

        var sim = current.Clone();
        var effective = plan.StartTime < sim.Time ? plan with { StartTime = sim.Time } : plan;
        var burn = new ProgrammedBurn(effective);
        var burned = sim.BeginBurn(effective.StartTime, sim.MassKg);
        var preBurn = new TimedState(sim.Time, sim.State);
        if (burned)
        {
            sim.AdvancePowered(effective.EndTime, burn, allowPartialStep: true);
            if (sim.Phase == FlightPhase.Powered) sim.EndBurn();
        }
        return Finish(sim, effective, burn, burned, preBurn, extent, out trace, drogue, main);
    }

    /// <summary>
    /// The rest of <see cref="Fly"/> once <paramref name="burn"/> is over in <paramref name="sim"/> (which this flies on),
    /// for callers that fly the burn themselves (<see cref="BurnLadder"/>).
    /// </summary>
    internal ManeuverPreview Finish(
        OrbitPropagator sim, ManeuverPlan effective, ProgrammedBurn burn, bool burned, TimedState preBurn, PreviewExtent extent,
        out PreviewTrace trace, ChuteState drogue = ChuteState.Stowed, ChuteState main = ChuteState.Stowed)
    {
        var postBurn = new TimedState(sim.Time, sim.State);
        var deltaV = sim.BurnDeltaVMps;
        var used = sim.BurnPropellantUsedKg;
        var remainingPropellant = Math.Max(0, effective.Engine.AvailablePropellantKg - used);
        var remainingDeltaV = remainingPropellant > NegligiblePropellantKg && sim.MassKg > remainingPropellant
            ? effective.Engine.ExhaustVelocityMps * Math.Log(sim.MassKg / (sim.MassKg - remainingPropellant))
            : 0;
        var conic = sim.Conic;
        var radius = sim.Body.RadiusM;
        TimedState? droguePoint = null;
        var entry = extent == PreviewExtent.Burn ? null : PredictEntry(sim, conic, drogue, main, extent, ref droguePoint);
        trace = new PreviewTrace(burned, preBurn, postBurn, new TimedState(sim.Time, sim.State), sim.Phase, sim.Body, droguePoint);
        return new ManeuverPreview(
            effective, deltaV, used, remainingDeltaV,
            effective.Engine.FixedDurationS is null && burn.PropellantExhausted,
            conic,
            (conic.PeriapsisM - radius) / 1000,
            (conic.ApoapsisM - radius) / 1000,
            entry);
    }

    /// <summary>
    /// Follows the trajectory across sphere-of-influence changes (e.g. Moon → Earth) to the first atmosphere it
    /// enters. The corridor angle is the inbound angle at the corridor's interface altitude on the coast conic, relative
    /// to the body being entered, of the arc that reaches the atmosphere.
    /// </summary>
    private EntryPrediction? PredictEntry(OrbitPropagator sim, Conic conic, ChuteState drogue, ChuteState main, PreviewExtent extent, ref TimedState? droguePoint)
    {
        var corridor = _mission.Corridor;
        var capsule = _mission.Capsule;
        if (sim.Phase == FlightPhase.Landed) return LandedWithoutEntry(sim);
        if (sim.Phase == FlightPhase.Coast && !MayReachAtmosphere(sim.Body, conic, corridor)) return null;

        var horizon = sim.Time + (long)(_mission.Planning.PreviewHorizonS * 1000);
        var drogueOpen = drogue != ChuteState.Stowed || sim.BallisticCoefficientKgPerM2 <= capsule.Drogue.BallisticCoefficientKgPerM2;
        var mainOpen = main != ChuteState.Stowed || sim.BallisticCoefficientKgPerM2 <= capsule.Main.BallisticCoefficientKgPerM2;
        var peakG = 0.0;
        var peakHeating = 0.0;
        var entered = sim.Phase == FlightPhase.Atmospheric;
        var interfaceTime = sim.Time;
        var interfaceSpeed = sim.State.SpeedMps;
        var flightPathAngle = entered ? sim.State.FlightPathAngleRad : 0;

        while (sim.Time < horizon && sim.Phase != FlightPhase.Landed)
        {
            if (sim.Phase != FlightPhase.Atmospheric)
            {
                var arc = sim.Phase == FlightPhase.Coast ? sim.Conic : conic;
                var arcBody = sim.Body;
                if (!sim.AdvanceToNextTransition(horizon)) break;
                if (sim.Phase == FlightPhase.Atmospheric && !entered)
                {
                    entered = true;
                    interfaceTime = sim.Time;
                    interfaceSpeed = sim.State.SpeedMps;
                    flightPathAngle = InboundFlightPathAngle(arc, arcBody.RadiusM + corridor.InterfaceAltitudeM);
                    if (extent == PreviewExtent.Interface) break;
                }
                else if (!entered && sim.Phase == FlightPhase.Coast && sim.Body != arcBody && !MayReachAtmosphere(sim.Body, sim.Conic, corridor))
                {
                    break;
                }
                continue;
            }

            sim.Advance(sim.Time + OrbitPropagator.IntegrationStepMs);
            if (sim.Phase != FlightPhase.Atmospheric) continue;
            peakG = Math.Max(peakG, sim.GLoad);
            peakHeating = Math.Max(peakHeating, sim.HeatingProxy);
            var altitude = sim.AltitudeM;
            if (droguePoint is null && altitude <= capsule.Drogue.NominalDeployAltitudeM)
            {
                droguePoint = new TimedState(sim.Time, sim.State);
                if (extent == PreviewExtent.Drogue) break;
            }
            var airspeed = sim.Body.AirVelocityOf(sim.State).Length;
            if (!drogueOpen && altitude <= capsule.Drogue.NominalDeployAltitudeM)
            {
                drogueOpen = true;
                if (capsule.Drogue.Evaluate(altitude, airspeed) == ChuteDeployResult.Deployed && !mainOpen)
                    sim.SetBallisticCoefficient(capsule.Drogue.BallisticCoefficientKgPerM2);
            }
            if (!mainOpen && altitude <= capsule.Main.NominalDeployAltitudeM)
            {
                mainOpen = true;
                if (capsule.Main.Evaluate(altitude, airspeed) == ChuteDeployResult.Deployed)
                    sim.SetBallisticCoefficient(capsule.Main.BallisticCoefficientKgPerM2);
            }
        }

        if (!entered) return null;
        var flightPathAngleDeg = flightPathAngle * Angles.RadToDeg;
        return new EntryPrediction(
            interfaceTime, flightPathAngleDeg, interfaceSpeed, corridor.Classify(flightPathAngleDeg),
            peakG, peakHeating, sim.Landing);
    }

    private static EntryPrediction? LandedWithoutEntry(OrbitPropagator sim) =>
        sim.Landing is { } landing
            ? new EntryPrediction(landing.Time, sim.State.FlightPathAngleRad * Angles.RadToDeg, sim.State.SpeedMps,
                CorridorQuality.TooSteep, sim.GLoad, sim.HeatingProxy, landing)
            : null;

    /// <summary>
    /// Whether a coast on <paramref name="conic"/> about <paramref name="body"/> can lead to an atmosphere: it dips
    /// below the corridor interface of this body, or it can leave the body's sphere of influence or reach a child's.
    /// </summary>
    private static bool MayReachAtmosphere(CelestialBody body, Conic conic, EntryCorridor corridor)
    {
        if (body.Atmosphere is not null && conic.PeriapsisM < body.RadiusM + corridor.InterfaceAltitudeM) return true;
        if (body.Parent is not null && conic.ApoapsisM >= body.SoiRadiusM) return true;
        var children = body.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (conic.ApoapsisM >= children[i].OrbitRadiusM - children[i].SoiRadiusM) return true;
        }
        return false;
    }

    /// <summary>Flight-path angle where the conic descends through <paramref name="radiusM"/> (true anomaly in (-π, 0)).</summary>
    private static double InboundFlightPathAngle(Conic conic, double radiusM)
    {
        var anomaly = conic.TrueAnomalyAtRadius(radiusM);
        return double.IsNaN(anomaly) ? 0 : conic.FlightPathAngleAtTrueAnomaly(-anomaly);
    }
}
