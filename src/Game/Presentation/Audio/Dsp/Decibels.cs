namespace Usa.Game.Presentation.Audio.Dsp;

public static class Decibels
{
    /// <summary>Level reported for silence, so comparisons and formatting never see infinity.</summary>
    public const float Floor = -120f;

    public static float ToGain(float db) => MathF.Pow(10f, db / 20f);

    public static float FromGain(float gain) => gain <= 1e-6f ? Floor : 20f * MathF.Log10(gain);
}
