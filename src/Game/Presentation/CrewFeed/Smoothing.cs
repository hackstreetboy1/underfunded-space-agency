namespace Usa.Game.Presentation.CrewFeed;

internal static class Smoothing
{
    /// <summary>First-order approach of <paramref name="current"/> to <paramref name="target"/> (frame-rate independent).</summary>
    public static float Approach(float current, float target, double deltaSeconds, double timeConstantSeconds)
    {
        if (timeConstantSeconds <= 0.0) return target;
        return current + ((target - current) * Factor(deltaSeconds, timeConstantSeconds));
    }

    public static float Factor(double deltaSeconds, double timeConstantSeconds) =>
        timeConstantSeconds <= 0.0 ? 1f : (float)(1.0 - Math.Exp(-Math.Max(0.0, deltaSeconds) / timeConstantSeconds));
}
