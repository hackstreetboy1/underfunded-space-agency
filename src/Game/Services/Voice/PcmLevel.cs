namespace Usa.Game.Services.Voice;

/// <summary>Level metering helpers for meters and lip-flap. Pure.</summary>
public static class PcmLevel
{
    /// <summary>Lower edge of the meter scale; anything quieter reads as 0.</summary>
    public const float FloorDb = -60f;

    public static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0f;
        double sum = 0;
        foreach (var sample in samples) sum += (double)sample * sample;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Maps a linear amplitude to 0..1 on a dB scale from <see cref="FloorDb"/> to 0 dBFS.</summary>
    public static float ToMeter(float linear)
    {
        if (linear <= 0f || float.IsNaN(linear)) return 0f;
        var db = 20f * MathF.Log10(linear);
        return Math.Clamp((db - FloorDb) / -FloorDb, 0f, 1f);
    }
}
