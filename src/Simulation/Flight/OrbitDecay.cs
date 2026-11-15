using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>
/// Drag on a coasting vehicle above the entry interface, applied analytically as speed losses at the apsides. The loss
/// over one revolution is King-Hele's orbit-averaged drag (density e-folding over the local scale height of
/// <see cref="AtmosphereModel.ThinDensityAt"/>); the part that lowers the apoapsis is removed at periapsis and the part
/// that lowers the periapsis at apoapsis, so an eccentric orbit circularises while its periapsis sinks, and a round orbit
/// spirals down evenly, until the periapsis reaches the interface and the integrated atmospheric flight takes over. A
/// vehicle left on a low orbit (e.g. after a retrofire without attitude control) therefore re-enters after a wait instead
/// of staying up forever (GDD 3.4: no hidden checkmates). Cheap, deterministic and independent of time compression.
/// </summary>
internal static class OrbitDecay
{
    /// <summary>Apse losses below this are not modelled (far above the thermosphere the orbit stays exactly Keplerian).</summary>
    public const double MinPassDeltaVMps = 1e-6;

    /// <summary>One apse never removes more than this fraction of the speed (the integrated model handles deep passes).</summary>
    private const double MaxPassSpeedFraction = 0.05;

    /// <summary>An orbit whose periapsis is less than this many (sea-level) scale heights inside the interface is grazing.</summary>
    private const double GrazingDepthScaleHeights = 3;

    /// <summary>Passes come every half revolution; after one, the next is searched no sooner than this many periods on.</summary>
    private const double MinPassSpacingOrbits = 0.375;

    /// <summary>
    /// The first apse strictly after <paramref name="after"/> at which drag is applied, and the speed it removes. Null when
    /// the orbit is airless, hyperbolic or too high to matter.
    /// </summary>
    /// <param name="lastPass">The previous pass on this orbit, if any: the next one is at least <see cref="MinPassSpacingOrbits"/> of a period later.</param>
    /// <remarks>
    /// Once the periapsis is inside the interface, the integrated atmospheric segment flies the dip, which takes over the
    /// periapsis share; a grazing orbit keeps the apoapsis share (with the density at the interface) so its periapsis keeps
    /// sinking at the same rate across the hand-over instead of skipping on for orbit after orbit. Steeper orbits are entry
    /// trajectories and are left to the integrated flight alone.
    /// </remarks>
    public static (MissionTime Time, double DeltaVMps)? Next(
        CelestialBody body, in Conic conic, double ballisticCoefficientKgPerM2, MissionTime after, MissionTime? lastPass = null)
    {
        if (body.Atmosphere is not { } atmosphere || conic.IsHyperbolic) return null;
        // Taking speed off at an apse of a near-circular orbit can make that very point the new orbit's other apse,
        // due milliseconds later; the next pass of the revolution is half an orbit on, so look from well past it.
        if (lastPass is { } last)
        {
            var earliest = last + (long)(conic.PeriodS * 1000 * MinPassSpacingOrbits);
            if (earliest > after) after = earliest;
        }
        var periapsisAltitudeM = conic.PeriapsisM - body.RadiusM;
        var dips = periapsisAltitudeM <= atmosphere.InterfaceAltitudeM;
        if (dips && periapsisAltitudeM < atmosphere.InterfaceAltitudeM - GrazingDepthScaleHeights * atmosphere.ScaleHeightM) return null;

        var (revolutionDeltaVMps, periapsisShare) = RevolutionLoss(
            body, atmosphere, conic, ballisticCoefficientKgPerM2, Math.Max(periapsisAltitudeM, atmosphere.InterfaceAltitudeM));
        var atPeriapsis = dips ? null : AtApse(conic, 0, revolutionDeltaVMps * periapsisShare, after);
        var atApoapsis = AtApse(conic, Math.PI, revolutionDeltaVMps * (1 - periapsisShare), after);
        if (atPeriapsis is not { } periapsis) return atApoapsis;
        if (atApoapsis is not { } apoapsis) return periapsis;
        return apoapsis.Time < periapsis.Time ? apoapsis : periapsis;
    }

    /// <summary>Removes <paramref name="deltaVMps"/> against the air-relative velocity.</summary>
    public static StateVector Apply(CelestialBody body, in StateVector state, double deltaVMps)
    {
        var air = body.AirVelocityOf(state);
        var airspeed = air.Length;
        return airspeed > 0 ? state with { VelocityMps = state.VelocityMps - air * (deltaVMps / airspeed) } : state;
    }

    /// <summary>
    /// Speed lost over one revolution, ½ρp vp²/β · T e^(-x) I0(x) with x = ae/H, and the share I0+I1 : I0-I1 of it that
    /// goes to the periapsis (lowering the apoapsis) rather than the apoapsis (King-Hele, first order in e).
    /// </summary>
    private static (double DeltaVMps, double PeriapsisShare) RevolutionLoss(
        CelestialBody body, AtmosphereModel atmosphere, in Conic conic, double ballisticCoefficientKgPerM2, double periapsisAltitudeM)
    {
        var density = atmosphere.ThinDensityAt(periapsisAltitudeM);
        var scaleHeightM = atmosphere.ThinScaleHeightAt(periapsisAltitudeM);
        var airspeed = body.AirVelocityOf(conic.StateAtTrueAnomaly(0)).Length;
        var x = conic.SemiMajorAxisM * conic.Eccentricity / scaleHeightM;
        var i0 = ScaledBesselI0(x);
        var i1 = ScaledBesselI1(x);
        var deltaV = 0.5 * density * airspeed * airspeed / ballisticCoefficientKgPerM2 * conic.PeriodS * i0;
        return (deltaV, 0.5 * (1 + i1 / i0));
    }

    private static (MissionTime Time, double DeltaVMps)? AtApse(in Conic conic, double trueAnomalyRad, double deltaVMps, MissionTime after)
    {
        if (!(deltaVMps >= MinPassDeltaVMps) || conic.NextTimeAtTrueAnomaly(trueAnomalyRad, after) is not { } time) return null;
        return (time, Math.Min(deltaVMps, MaxPassSpeedFraction * conic.StateAtTrueAnomaly(trueAnomalyRad).SpeedMps));
    }

    /// <summary>e^(-x) I0(x) for x ≥ 0 (Abramowitz and Stegun 9.8.1 and 9.8.2, relative error below 2e-7).</summary>
    private static double ScaledBesselI0(double x)
    {
        if (x < 3.75)
        {
            var t = x / 3.75 * (x / 3.75);
            return Math.Exp(-x) * (1 + t * (3.5156229 + t * (3.0899424 + t * (1.2067492 + t * (0.2659732 + t * (0.0360768 + t * 0.0045813))))));
        }
        var u = 3.75 / x;
        return (0.39894228 + u * (0.01328592 + u * (0.00225319 + u * (-0.00157565 + u * (0.00916281
            + u * (-0.02057706 + u * (0.02635537 + u * (-0.01647633 + u * 0.00392377)))))))) / Math.Sqrt(x);
    }

    /// <summary>e^(-x) I1(x) for x ≥ 0 (Abramowitz and Stegun 9.8.3 and 9.8.4).</summary>
    private static double ScaledBesselI1(double x)
    {
        if (x < 3.75)
        {
            var t = x / 3.75 * (x / 3.75);
            return Math.Exp(-x) * x * (0.5 + t * (0.87890594 + t * (0.51498869 + t * (0.15084934 + t * (0.02658733 + t * (0.00301532 + t * 0.00032411))))));
        }
        var u = 3.75 / x;
        return (0.39894228 + u * (-0.03988024 + u * (-0.00362018 + u * (0.00163801 + u * (-0.01031555
            + u * (0.02282967 + u * (-0.02895312 + u * (0.01787654 - u * 0.00420059)))))))) / Math.Sqrt(x);
    }
}
