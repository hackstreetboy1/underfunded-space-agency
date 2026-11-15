namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>One-pole smoother for parameters and slow modulation (time constant in seconds).</summary>
public sealed class Smoother
{
    private readonly float _coefficient;

    public Smoother(double timeConstantSeconds, float initial = 0f)
    {
        _coefficient = timeConstantSeconds <= 0.0
            ? 1f
            : (float)(1.0 - Math.Exp(-1.0 / (timeConstantSeconds * AudioFormat.SampleRate)));
        Value = initial;
    }

    public float Value { get; private set; }

    public float Next(float target)
    {
        Value += (target - Value) * _coefficient;
        return Value;
    }

    public void Snap(float value) => Value = value;
}
