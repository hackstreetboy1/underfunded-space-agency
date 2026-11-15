using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Flight;

/// <summary>Kepler's equation for elliptic (e &lt; 1) and hyperbolic (e &gt; 1) orbits.</summary>
public static class Kepler
{
    private const int MaxIterations = 60;
    private const double Tolerance = 1e-14;

    public static double TrueFromMean(double meanRad, double eccentricity) =>
        eccentricity < 1
            ? TrueFromEccentric(SolveElliptic(Angles.Wrap(meanRad), eccentricity), eccentricity)
            : TrueFromHyperbolic(SolveHyperbolic(meanRad, eccentricity), eccentricity);

    /// <summary>Mean anomaly for a true anomaly. Elliptic results lie in (-π, π].</summary>
    public static double MeanFromTrue(double trueRad, double eccentricity)
    {
        if (eccentricity < 1)
        {
            var eccentric = 2 * Math.Atan2(
                Math.Sqrt(1 - eccentricity) * Math.Sin(trueRad / 2),
                Math.Sqrt(1 + eccentricity) * Math.Cos(trueRad / 2));
            return eccentric - eccentricity * Math.Sin(eccentric);
        }

        var hyperbolic = 2 * Math.Atanh(Math.Sqrt((eccentricity - 1) / (eccentricity + 1)) * Math.Tan(trueRad / 2));
        return eccentricity * Math.Sinh(hyperbolic) - hyperbolic;
    }

    /// <summary>Solves M = E - e sin E for E, with M in (-π, π].</summary>
    public static double SolveElliptic(double meanRad, double eccentricity)
    {
        // Danby's starter converges for every e < 1, unlike E0 = M near e = 1.
        var anomaly = meanRad + 0.85 * eccentricity * Math.Sign(Math.Sin(meanRad));
        for (var i = 0; i < MaxIterations; i++)
        {
            var step = (anomaly - eccentricity * Math.Sin(anomaly) - meanRad) / (1 - eccentricity * Math.Cos(anomaly));
            anomaly -= step;
            if (Math.Abs(step) < Tolerance) break;
        }
        return anomaly;
    }

    /// <summary>Solves M = e sinh H - H for H.</summary>
    public static double SolveHyperbolic(double meanRad, double eccentricity)
    {
        var anomaly = Math.Sign(meanRad) * Math.Log(2 * Math.Abs(meanRad) / eccentricity + 1.8);
        for (var i = 0; i < MaxIterations; i++)
        {
            var step = (eccentricity * Math.Sinh(anomaly) - anomaly - meanRad) / (eccentricity * Math.Cosh(anomaly) - 1);
            anomaly -= step;
            if (Math.Abs(step) < Tolerance * Math.Max(1, Math.Abs(anomaly))) break;
        }
        return anomaly;
    }

    private static double TrueFromEccentric(double eccentricRad, double eccentricity) =>
        2 * Math.Atan2(
            Math.Sqrt(1 + eccentricity) * Math.Sin(eccentricRad / 2),
            Math.Sqrt(1 - eccentricity) * Math.Cos(eccentricRad / 2));

    private static double TrueFromHyperbolic(double hyperbolicRad, double eccentricity) =>
        2 * Math.Atan(Math.Sqrt((eccentricity + 1) / (eccentricity - 1)) * Math.Tanh(hyperbolicRad / 2));
}
