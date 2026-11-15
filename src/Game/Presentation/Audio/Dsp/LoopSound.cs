namespace Usa.Game.Presentation.Audio.Dsp;

/// <summary>
/// Base for continuous sounds. A seamless loop table (built once per sound and shared) is played
/// cyclically through an optional real-time <see cref="Shape"/> stage driven by the smoothed
/// intensity, with fade-in on start and fade-out on <see cref="Stop"/>.
/// </summary>
public abstract class LoopSound : ISoundGenerator
{
    /// <summary>Loop tables must divide this length so 8 s previews wrap seamlessly.</summary>
    public const double PreviewSeconds = 8.0;

    private readonly float[] _table;
    private readonly Smoother _intensity;
    private readonly float _fadeInStep;
    private readonly float _fadeOutStep;
    private float _targetIntensity;
    private int _position;
    private float _fade;
    private bool _stopping;

    protected LoopSound(float[] table, float intensity, double fadeInSeconds, double fadeOutSeconds)
    {
        ArgumentNullException.ThrowIfNull(table);
        _table = table;
        _targetIntensity = Math.Clamp(intensity, 0f, 1f);
        _intensity = new Smoother(0.15, _targetIntensity);
        _fadeInStep = Step(fadeInSeconds);
        _fadeOutStep = Step(fadeOutSeconds);
    }

    public abstract SoundId Id { get; }

    public bool IsLooping => true;

    public bool IsFinished { get; private set; }

    public float Intensity
    {
        get => _targetIntensity;
        set => _targetIntensity = Math.Clamp(value, 0f, 1f);
    }

    public int LoopLengthSamples => _table.Length;

    public void Render(Span<float> output)
    {
        for (var i = 0; i < output.Length; i++)
        {
            if (IsFinished)
            {
                output[i..].Clear();
                return;
            }

            var sample = Shape(_table[_position], _intensity.Next(_targetIntensity));
            if (++_position == _table.Length) _position = 0;
            if (_stopping)
            {
                _fade -= _fadeOutStep;
                if (_fade <= 0f)
                {
                    _fade = 0f;
                    IsFinished = true;
                }
            }
            else if (_fade < 1f)
            {
                _fade = Math.Min(1f, _fade + _fadeInStep);
            }

            output[i] = sample * _fade;
        }
    }

    public void Stop() => _stopping = true;

    /// <summary>Real-time stage applied to every table sample; must be time-invariant for a fixed intensity.</summary>
    protected virtual float Shape(float sample, float intensity) => sample * intensity;

    protected static int TableLength(double seconds)
    {
        var length = AudioFormat.ToSamples(seconds);
        if (AudioFormat.ToSamples(PreviewSeconds) % length != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Loop length must divide the preview length.");
        }

        return length;
    }

    /// <summary>
    /// Builds a seamless noise layer: <paramref name="next"/> is run for a warm-up (to settle
    /// filters), then for the loop plus a crossfade overhang that is folded over the loop start.
    /// </summary>
    protected static float[] BuildNoiseLayer(int length, Func<float> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var warmUp = AudioFormat.ToSamples(0.5);
        var crossfade = Math.Min(length / 4, AudioFormat.ToSamples(0.5));
        for (var i = 0; i < warmUp; i++)
        {
            next();
        }

        var source = new float[length + crossfade];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = next();
        }

        var loop = new float[length];
        Signal.FoldSeamless(source, loop);
        return loop;
    }

    private static float Step(double seconds) =>
        seconds <= 0.0 ? 1f : (float)(1.0 / (seconds * AudioFormat.SampleRate));
}
