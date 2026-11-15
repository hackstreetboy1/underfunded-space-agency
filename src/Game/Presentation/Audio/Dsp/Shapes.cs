namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>Stateless envelope shapes evaluated at a time in seconds.</summary>
public static class Shapes
{
    /// <summary>Linear attack then exponential decay with time constant <paramref name="tauSeconds"/>.</summary>
    public static float AttackDecay(double t, double attackSeconds, double tauSeconds)
    {
        if (t < 0.0) return 0f;
        if (t < attackSeconds) return (float)(t / attackSeconds);
        return (float)Math.Exp(-(t - attackSeconds) / tauSeconds);
    }

    /// <summary>A gated tone: raised-cosine ramps of <paramref name="rampSeconds"/> at both ends.</summary>
    public static float Gate(double t, double start, double length, double rampSeconds)
    {
        var local = t - start;
        if (local <= 0.0 || local >= length) return 0f;
        var edge = Math.Min(local, length - local);
        return edge >= rampSeconds ? 1f : (float)(0.5 - (0.5 * Math.Cos(Math.PI * edge / rampSeconds)));
    }

    public static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
