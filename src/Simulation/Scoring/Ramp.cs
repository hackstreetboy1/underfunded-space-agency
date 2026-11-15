namespace Usa.Simulation.Scoring;

internal static class Ramp
{
    /// <summary>1 at or below <paramref name="full"/>, 0 at or above <paramref name="zero"/>, linear between.</summary>
    public static double Down(double value, double full, double zero)
    {
        if (value <= full) return 1.0;
        if (value >= zero) return 0.0;
        return (zero - value) / (zero - full);
    }

    public static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    public static double Ratio(double part, double whole) => whole > 0 ? Clamp01(part / whole) : 0.0;
}
