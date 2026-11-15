namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>
/// Base for short sounds. The whole sound is synthesized on first render (at most a couple of
/// seconds), edge-faded and peak-normalized to its authored level, then streamed. A pre-rendered variant from
/// <see cref="SoundFactory.Prewarm"/> can be handed in with <see cref="UseRendered"/> to skip synthesis entirely.
/// </summary>
public abstract class OneShotSound(uint seed) : ISoundGenerator
{
    private const double StopFadeSeconds = 0.02;
    private float[]? _samples;
    private int _position;
    private int _stopFadeLength;
    private int _stopFadeRemaining = -1;

    public abstract SoundId Id { get; }

    public bool IsLooping => false;

    public bool IsFinished { get; private set; }

    public float Intensity { get; set; } = 1f;

    public int LengthSamples => Samples.Length;

    protected abstract double DurationSeconds { get; }

    /// <summary>Authored peak level in dBFS; the bus and voice gains are applied on top.</summary>
    protected abstract float PeakDb { get; }

    protected virtual double FadeInSeconds => 0.0005;

    protected virtual double FadeOutSeconds => 0.012;

    private float[] Samples => _samples ??= Build();

    /// <summary>The finished samples (synthesized now if needed); shared, never written after this.</summary>
    public float[] Rendered => Samples;

    /// <summary>Streams <paramref name="samples"/> (a shared pre-rendered variant of this sound) instead of synthesizing.</summary>
    public void UseRendered(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (_samples is not null) throw new InvalidOperationException("The sound has already been rendered.");
        _samples = samples;
    }

    public void Render(Span<float> output)
    {
        var samples = Samples;
        var gain = Math.Clamp(Intensity, 0f, 1f);
        for (var i = 0; i < output.Length; i++)
        {
            if (IsFinished)
            {
                output[i..].Clear();
                return;
            }

            var fade = 1f;
            if (_stopFadeRemaining >= 0)
            {
                fade = (float)_stopFadeRemaining / _stopFadeLength;
                if (_stopFadeRemaining-- == 0) IsFinished = true;
            }

            output[i] = samples[_position] * gain * fade;
            if (++_position >= samples.Length) IsFinished = true;
        }
    }

    public void Stop()
    {
        if (IsFinished || _stopFadeRemaining >= 0) return;
        _stopFadeLength = AudioFormat.ToSamples(StopFadeSeconds);
        _stopFadeRemaining = _stopFadeLength;
    }

    protected abstract void Synthesize(Span<float> output, SynthRandom random);

    protected static double TimeAt(int sampleIndex) => sampleIndex * AudioFormat.SamplePeriodSeconds;

    private float[] Build()
    {
        var samples = new float[AudioFormat.ToSamples(DurationSeconds)];
        Synthesize(samples, new SynthRandom(seed));
        Signal.FadeEdges(samples, FadeInSeconds, FadeOutSeconds);
        Signal.NormalizePeak(samples, PeakDb);
        return samples;
    }
}
