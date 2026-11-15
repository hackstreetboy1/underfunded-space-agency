using Usa.Simulation.Kernel;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.LifeSupport.Thermal;

/// <summary>
/// Pure solar-geometry helpers that turn a vehicle's sun direction and attitude into a node's
/// <see cref="ThermalInputs.SolarExposure"/> and <see cref="ThermalInputs.SolarAbsorbedW"/>. Deterministic and allocation-free.
/// </summary>
public static class SolarHeating
{
    /// <summary>Mean solar flux at 1 AU.</summary>
    public const double SolarConstantWPerM2 = 1361.0;

    /// <summary>Cosine of the angle between the Sun and a surface's outward normal; 0 when the surface faces away.</summary>
    public static double Incidence(Vec3d sunBody, Vec3d normalBody)
    {
        var lengths = Math.Sqrt(sunBody.LengthSquared * normalBody.LengthSquared);
        if (!(lengths > 0) || !double.IsFinite(lengths)) return 0;
        return Math.Clamp(sunBody.Dot(normalBody) / lengths, 0.0, 1.0);
    }

    /// <summary>Σ flux · α · A · incidence over the surfaces: the heat they absorb in full sun.</summary>
    public static double AbsorbedW(Vec3d sunBody, IReadOnlyList<SolarSurfaceSpec> surfaces, double fluxWPerM2 = SolarConstantWPerM2)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        var total = 0.0;
        for (var i = 0; i < surfaces.Count; i++)
        {
            var surface = surfaces[i];
            total += surface.Absorptivity * surface.AreaM2 * Incidence(sunBody, surface.NormalBody);
        }
        return total * fluxWPerM2;
    }

    /// <summary>The curve's factor at |<paramref name="sunPitchDeg"/>|, interpolated linearly and flat beyond the end points.</summary>
    public static double PitchFactor(IReadOnlyList<SolarCurvePoint> curve, double sunPitchDeg)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (curve.Count == 0) return 1.0;
        var x = Math.Abs(sunPitchDeg);
        if (x <= curve[0].SunPitchDeg) return curve[0].Factor;
        for (var i = 1; i < curve.Count; i++)
        {
            var b = curve[i];
            if (x > b.SunPitchDeg) continue;
            var a = curve[i - 1];
            return a.Factor + (b.Factor - a.Factor) * (x - a.SunPitchDeg) / (b.SunPitchDeg - a.SunPitchDeg);
        }
        return curve[^1].Factor;
    }

    /// <summary>A node's exposure share and absorbed surface heat for <paramref name="sun"/>.</summary>
    public static (double Exposure, double AbsorbedW) Evaluate(NodeSolarSpec spec, SunView sun, double fluxWPerM2 = SolarConstantWPerM2)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var exposure = spec.PitchCurve is { } curve ? PitchFactor(curve, sun.SunPitchDeg) : 1.0;
        var absorbed = spec.Surfaces is { } surfaces ? AbsorbedW(sun.SunBody, surfaces, fluxWPerM2) : 0.0;
        return (exposure, absorbed);
    }

    /// <summary>True when the view can be evaluated: a finite sun vector and a finite pitch.</summary>
    public static bool IsUsable(in SunView sun) =>
        double.IsFinite(sun.SunPitchDeg) && double.IsFinite(sun.SunBody.X) && double.IsFinite(sun.SunBody.Y) && double.IsFinite(sun.SunBody.Z);

    /// <summary>
    /// Writes <see cref="ThermalInputs.SolarExposure"/> and <see cref="ThermalInputs.SolarAbsorbedW"/> for every node that has a
    /// <see cref="NodeSolarSpec"/>, using the Sun as seen from that node's vehicle. When <paramref name="sunFor"/> does not know the
    /// node's vehicle (null) or its view is not finite, the node gets the defaults (exposure 1, absorbed 0), so the inputs are always
    /// a function of this step's state and never carry an old value. Nodes without a spec are not touched. The caller still sets
    /// <see cref="ThermalInputs.SunlitFraction"/>.
    /// </summary>
    /// <returns>How many nodes with a spec fell back to the defaults.</returns>
    /// <param name="sunFor">The Sun for a node's id (integration maps the id to its vehicle); create the delegate once, not per tick.</param>
    public static int Fill(ThermalNetwork network, ThermalInputs inputs, Func<ComponentId, SunView?> sunFor, double fluxWPerM2 = SolarConstantWPerM2)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(sunFor);
        var exposure = inputs.SolarExposure;
        var absorbed = inputs.SolarAbsorbedW;
        var defaulted = 0;
        for (var i = 0; i < network.NodeCount; i++)
        {
            if (network.Solar(i) is not { } spec) continue;
            if (sunFor(network.NodeId(i)) is { } sun && IsUsable(sun))
            {
                (exposure[i], absorbed[i]) = Evaluate(spec, sun, fluxWPerM2);
                continue;
            }
            exposure[i] = 1.0;
            absorbed[i] = 0.0;
            defaulted++;
        }
        return defaulted;
    }
}
