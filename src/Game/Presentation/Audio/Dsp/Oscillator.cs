namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>
/// Phase-accumulator oscillator. Square and saw are band-limited with PolyBLEP so alarm and UI
/// tones stay clean instead of aliasing into harsh partials.
/// </summary>
public sealed class Oscillator(double frequencyHz = 440.0, double phase = 0.0)
{
    private double _phase = phase;

    public double FrequencyHz { get; set; } = frequencyHz;

    public void ResetPhase(double phase = 0.0) => _phase = phase;

    public float Sine()
    {
        var value = (float)Math.Sin(2.0 * Math.PI * _phase);
        Advance();
        return value;
    }

    public float Triangle()
    {
        var value = (float)(4.0 * Math.Abs(_phase - 0.5) - 1.0);
        Advance();
        return value;
    }

    public float Saw()
    {
        var increment = Increment;
        var value = (float)((2.0 * _phase) - 1.0 - PolyBlep(_phase, increment));
        Advance();
        return value;
    }

    public float Square()
    {
        var increment = Increment;
        var value = _phase < 0.5 ? 1.0 : -1.0;
        value += PolyBlep(_phase, increment);
        value -= PolyBlep((_phase + 0.5) % 1.0, increment);
        Advance();
        return (float)value;
    }

    /// <summary>Sine plus weighted harmonics; <paramref name="harmonicGains"/>[0] is the 2nd harmonic.</summary>
    public float Additive(ReadOnlySpan<float> harmonicGains)
    {
        var angle = 2.0 * Math.PI * _phase;
        var value = Math.Sin(angle);
        for (var i = 0; i < harmonicGains.Length; i++)
        {
            value += harmonicGains[i] * Math.Sin(angle * (i + 2));
        }

        Advance();
        return (float)value;
    }

    private double Increment => FrequencyHz / AudioFormat.SampleRate;

    private void Advance()
    {
        _phase += Increment;
        _phase -= Math.Floor(_phase);
    }

    private static double PolyBlep(double t, double dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - (t * t) - 1.0;
        }

        if (t > 1.0 - dt)
        {
            t = (t - 1.0) / dt;
            return (t * t) + t + t + 1.0;
        }

        return 0.0;
    }
}
