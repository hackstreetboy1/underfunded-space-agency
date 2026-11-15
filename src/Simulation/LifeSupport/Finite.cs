namespace Usa.Simulation.LifeSupport;

/// <summary>Boundary guard: NaN or ±∞ would corrupt life-support state permanently, so numeric inputs are rejected.</summary>
internal static class Finite
{
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite.</exception>
    public static double Require(double value, string name) =>
        double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, $"'{name}' must be a finite number.");
}
