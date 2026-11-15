namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>
/// Zero-lookahead safety limiter: instant attack guarantees no sample exceeds the ceiling, a smooth
/// release avoids pumping. Meant as a guard; well-gained material never touches it.
/// </summary>
public sealed class Limiter(float ceilingDb = -1f, double releaseSeconds = 0.08)
{
    private readonly float _ceiling = Decibels.ToGain(ceilingDb);
    private readonly float _releaseCoefficient = (float)Math.Exp(-1.0 / (releaseSeconds * AudioFormat.SampleRate));
    private float _gain = 1f;

    /// <summary>Deepest gain reduction applied so far, in dB (0 when never engaged).</summary>
    public float MaxReductionDb { get; private set; }

    public float Process(float input)
    {
        var magnitude = MathF.Abs(input);
        var needed = magnitude > _ceiling ? _ceiling / magnitude : 1f;
        _gain = needed < _gain ? needed : 1f - ((1f - _gain) * _releaseCoefficient);
        if (_gain > needed) _gain = needed;
        MaxReductionDb = Math.Min(MaxReductionDb, Decibels.FromGain(_gain));
        return input * _gain;
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Process(buffer[i]);
        }
    }
}
