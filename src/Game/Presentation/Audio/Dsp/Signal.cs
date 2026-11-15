namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>Whole-buffer helpers used when authoring sounds and loop tables.</summary>
public static class Signal
{
    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, MathF.Abs(sample));
        }

        return peak;
    }

    public static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0f;
        var sum = 0.0;
        foreach (var sample in samples)
        {
            sum += sample * (double)sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    public static void Scale(Span<float> samples, float gain)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
    }

    public static void NormalizePeak(Span<float> samples, float targetPeakDb)
    {
        var peak = Peak(samples);
        if (peak > 0f) Scale(samples, Decibels.ToGain(targetPeakDb) / peak);
    }

    /// <summary>Scales to an RMS target but never lets the peak exceed <paramref name="maxPeakDb"/>.</summary>
    public static void NormalizeRms(Span<float> samples, float targetRmsDb, float maxPeakDb)
    {
        var rms = Rms(samples);
        var peak = Peak(samples);
        if (rms <= 0f) return;
        var gain = Math.Min(Decibels.ToGain(targetRmsDb) / rms, Decibels.ToGain(maxPeakDb) / peak);
        Scale(samples, gain);
    }

    /// <summary>Raised-cosine fades at the edges so one-shots never start or end with a click.</summary>
    public static void FadeEdges(Span<float> samples, double fadeInSeconds, double fadeOutSeconds)
    {
        var fadeIn = Math.Min(samples.Length, AudioFormat.ToSamples(fadeInSeconds));
        for (var i = 0; i < fadeIn; i++)
        {
            samples[i] *= (float)(0.5 - (0.5 * Math.Cos(Math.PI * i / fadeIn)));
        }

        var fadeOut = Math.Min(samples.Length, AudioFormat.ToSamples(fadeOutSeconds));
        for (var i = 0; i < fadeOut; i++)
        {
            samples[samples.Length - 1 - i] *= (float)(0.5 - (0.5 * Math.Cos(Math.PI * i / fadeOut)));
        }
    }

    /// <summary>
    /// Folds a source of <c>loop.Length + crossfade</c> samples into a seamless loop: the overhang
    /// after the loop end is equal-power crossfaded over the loop start, so the wrap from the last
    /// sample to the first continues the source exactly. For uncorrelated (noise) material.
    /// </summary>
    public static void FoldSeamless(ReadOnlySpan<float> source, Span<float> loop)
    {
        var crossfade = source.Length - loop.Length;
        if (crossfade < 0) throw new ArgumentException("Source must be at least as long as the loop.", nameof(source));
        source[..loop.Length].CopyTo(loop);
        for (var i = 0; i < crossfade; i++)
        {
            var angle = 0.5 * Math.PI * i / crossfade;
            loop[i] = (float)((source[loop.Length + i] * Math.Cos(angle)) + (source[i] * Math.Sin(angle)));
        }
    }

    public static void Add(Span<float> destination, ReadOnlySpan<float> source, float gain = 1f)
    {
        var count = Math.Min(destination.Length, source.Length);
        for (var i = 0; i < count; i++)
        {
            destination[i] += source[i] * gain;
        }
    }
}
