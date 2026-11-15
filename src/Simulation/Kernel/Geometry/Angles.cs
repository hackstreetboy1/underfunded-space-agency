namespace Usa.Simulation.Kernel.Geometry;

public static class Angles
{
    public const double TwoPi = 2 * Math.PI;
    public const double DegToRad = Math.PI / 180.0;
    public const double RadToDeg = 180.0 / Math.PI;

    /// <summary>Wraps to (-π, π].</summary>
    public static double Wrap(double angleRad)
    {
        var wrapped = Math.IEEERemainder(angleRad, TwoPi);
        return wrapped <= -Math.PI ? wrapped + TwoPi : wrapped;
    }

    /// <summary>Wraps to [0, 2π).</summary>
    public static double WrapPositive(double angleRad)
    {
        var wrapped = angleRad % TwoPi;
        return wrapped < 0 ? wrapped + TwoPi : wrapped;
    }
}
