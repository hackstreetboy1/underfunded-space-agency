namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>
/// Smooth random modulation in roughly [-1, 1]: random targets picked at <c>rateHz</c> and glided
/// to, so beds breathe without a repeating LFO pattern.
/// </summary>
public sealed class Flutter
{
    private readonly SynthRandom _random;
    private readonly int _holdSamples;
    private readonly Smoother _smoother;
    private int _countdown;
    private float _target;

    public Flutter(uint seed, double rateHz)
    {
        _random = new SynthRandom(seed);
        _holdSamples = Math.Max(1, (int)(AudioFormat.SampleRate / rateHz));
        _smoother = new Smoother(0.5 / rateHz);
    }

    public float Next()
    {
        if (--_countdown <= 0)
        {
            _countdown = _holdSamples;
            _target = _random.NextBipolar();
        }

        return _smoother.Next(_target);
    }
}
