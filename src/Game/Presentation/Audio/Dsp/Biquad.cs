namespace Usa.Game.Presentation.Audio.Dsp;

public enum FilterKind
{
    LowPass,
    HighPass,
    BandPass,
}

/// <summary>RBJ cookbook biquad (transposed direct form II). Band-pass has 0 dB peak gain.</summary>
public sealed class Biquad
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    public Biquad(FilterKind kind, double frequencyHz, double q = 0.7071) => Configure(kind, frequencyHz, q);

    public FilterKind Kind { get; private set; }

    public void Configure(FilterKind kind, double frequencyHz, double q = 0.7071)
    {
        Kind = kind;
        var frequency = Math.Clamp(frequencyHz, 5.0, AudioFormat.SampleRate * 0.45);
        var omega = 2.0 * Math.PI * frequency / AudioFormat.SampleRate;
        var cos = Math.Cos(omega);
        var alpha = Math.Sin(omega) / (2.0 * Math.Max(q, 0.05));
        double b0, b1, b2;
        switch (kind)
        {
            case FilterKind.LowPass:
                b0 = (1.0 - cos) / 2.0;
                b1 = 1.0 - cos;
                b2 = b0;
                break;
            case FilterKind.HighPass:
                b0 = (1.0 + cos) / 2.0;
                b1 = -(1.0 + cos);
                b2 = b0;
                break;
            default:
                b0 = alpha;
                b1 = 0.0;
                b2 = -alpha;
                break;
        }

        var a0 = 1.0 + alpha;
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = -2.0 * cos / a0;
        _a2 = (1.0 - alpha) / a0;
    }

    public float Process(float input)
    {
        var output = (_b0 * input) + _z1;
        _z1 = (_b1 * input) - (_a1 * output) + _z2;
        _z2 = (_b2 * input) - (_a2 * output);
        return (float)output;
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Process(buffer[i]);
        }
    }

    public void Reset() => _z1 = _z2 = 0.0;
}
