using Usa.Simulation.Data;
using Usa.Simulation.Kernel.Geometry;
using Usa.Simulation.LifeSupport.Config;

namespace Usa.Simulation.LifeSupport.Thermal;

/// <summary>
/// How a thermal node's solar heating depends on attitude. Either form (or both) may be given:
/// <see cref="PitchCurve"/> scales the node's <see cref="ThermalNodeSpec.SolarAbsorbedPeakW"/> by |sun pitch|, and
/// <see cref="Surfaces"/> add the heat absorbed by sunlit outer surfaces from the body-frame sun direction.
/// </summary>
/// <param name="PitchCurve">Factor by |sun pitch| in degrees (0 = arrays square to the Sun), interpolated, flat beyond the ends.</param>
public sealed record NodeSolarSpec(
    IReadOnlyList<SolarCurvePoint>? PitchCurve = null,
    IReadOnlyList<SolarSurfaceSpec>? Surfaces = null)
{
    public void Validate(DefinitionErrors errors, string file, string path)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var check = new DataChecks(errors, file);
        if (PitchCurve is { } curve)
        {
            if (curve.Count == 0) errors.Add(file, path + ".pitchCurve", "A pitch curve needs at least one point.");
            for (var i = 0; i < curve.Count; i++)
            {
                var point = $"{path}.pitchCurve[{i}]";
                check.InRange(point + ".sunPitchDeg", curve[i].SunPitchDeg, 0, 180);
                check.NotNegative(point + ".factor", curve[i].Factor);
                if (i > 0 && !(curve[i].SunPitchDeg > curve[i - 1].SunPitchDeg))
                    errors.Add(file, point + ".sunPitchDeg", "Pitch curve points must be in ascending pitch order.");
            }
        }
        if (Surfaces is not { } surfaces) return;
        for (var i = 0; i < surfaces.Count; i++)
        {
            var surface = $"{path}.surfaces[{i}]";
            var normal = surfaces[i].NormalBody;
            if (!(double.IsFinite(normal.X) && double.IsFinite(normal.Y) && double.IsFinite(normal.Z)) || normal.LengthSquared < 1e-12)
                errors.Add(file, surface + ".normal", "The surface normal must be a finite, non-zero vector.");
            check.Positive(surface + ".areaM2", surfaces[i].AreaM2);
            check.InRange(surface + ".absorptivity", surfaces[i].Absorptivity, 0, 1);
        }
    }
}

/// <summary>One point of a <see cref="NodeSolarSpec.PitchCurve"/>.</summary>
public readonly record struct SolarCurvePoint(double SunPitchDeg, double Factor);

/// <summary>A flat outer surface heating a node: outward normal in the vehicle body frame, area and solar absorptivity (α).</summary>
public sealed record SolarSurfaceSpec(Vec3d NormalBody, double AreaM2, double Absorptivity);

/// <summary>The Sun as seen from one vehicle.</summary>
/// <param name="SunBody">Direction to the Sun in the vehicle body frame (any length; zero means no Sun direction).</param>
/// <param name="SunPitchDeg">Attitude's sun pitch, −180…180, 0 when the arrays face the Sun.</param>
public readonly record struct SunView(Vec3d SunBody, double SunPitchDeg);
