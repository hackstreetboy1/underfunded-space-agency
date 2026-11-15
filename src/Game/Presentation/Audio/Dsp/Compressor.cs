namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>Feed-forward peak compressor with a smoothed gain computer (hard knee).</summary>
public sealed class Compressor
{
    private readonly float _thresholdDb;
    private readonly float _ratio;
    private readonly float _makeupDb;
    private readonly float _attackCoefficient;
    private readonly float _releaseCoefficient;
    private float _reductionDb;

    public Compressor(float thresholdDb, float ratio, double attackSeconds, double releaseSeconds, float makeupDb = 0f)
    {
        _thresholdDb = thresholdDb;
        _ratio = Math.Max(1f, ratio);
        _makeupDb = makeupDb;
        _attackCoefficient = TimeCoefficient(attackSeconds);
        _releaseCoefficient = TimeCoefficient(releaseSeconds);
    }

    /// <summary>Current gain reduction in dB (0 or negative).</summary>
    public float GainReductionDb => -_reductionDb;

    public float Process(float input)
    {
        var levelDb = Decibels.FromGain(MathF.Abs(input));
        var over = levelDb - _thresholdDb;
        var target = over > 0f ? over - (over / _ratio) : 0f;
        var coefficient = target > _reductionDb ? _attackCoefficient : _releaseCoefficient;
        _reductionDb = target + ((_reductionDb - target) * coefficient);
        return input * Decibels.ToGain(_makeupDb - _reductionDb);
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Process(buffer[i]);
        }
    }

    private static float TimeCoefficient(double seconds) =>
        seconds <= 0.0 ? 0f : (float)Math.Exp(-1.0 / (seconds * AudioFormat.SampleRate));
}
