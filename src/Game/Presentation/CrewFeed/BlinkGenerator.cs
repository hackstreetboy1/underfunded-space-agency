using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.CrewFeed;

/// <summary>
/// Seeded blinks every 3-6 s (MODELING 4.4), or the eyes held closed while impaired. Blink starts are scheduled on
/// accumulated time, not per frame, so the same seed gives the same blinks at any frame rate.
/// </summary>
public sealed class BlinkGenerator
{
    private readonly Pcg32 _random;
    private readonly CrewFeedSettings _settings;
    private double _timeSeconds;
    private double _nextBlinkSeconds;
    private double _blinkStartSeconds = double.NegativeInfinity;
    private float _held;
    private bool _holding;

    public BlinkGenerator(Pcg32 random, CrewFeedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(settings);
        _random = random;
        _settings = settings;
        _nextBlinkSeconds = NextInterval();
    }

    public int BlinkCount { get; private set; }

    /// <summary>Start of the latest blink on this generator's own time line (seconds since creation).</summary>
    public double LastBlinkStartSeconds => _blinkStartSeconds;

    public float Weight { get; private set; }

    public float Update(double deltaSeconds, bool holdClosed)
    {
        _timeSeconds += Math.Max(0.0, deltaSeconds);
        _held = Smoothing.Approach(_held, holdClosed ? _settings.ImpairedBlinkWeight : 0f, deltaSeconds, _settings.BlinkHoldTimeConstantSeconds);
        if (holdClosed)
        {
            _holding = true;
        }
        else
        {
            // Blinking resumes a fresh interval after the eyes reopen.
            if (_holding) _nextBlinkSeconds = _timeSeconds + NextInterval();
            _holding = false;
            while (_timeSeconds >= _nextBlinkSeconds)
            {
                _blinkStartSeconds = _nextBlinkSeconds;
                BlinkCount++;
                _nextBlinkSeconds += NextInterval();
            }
        }

        Weight = Math.Max(Shape(_timeSeconds - _blinkStartSeconds), _held);
        return Weight;
    }

    private double NextInterval() =>
        _settings.BlinkMinIntervalSeconds + ((_settings.BlinkMaxIntervalSeconds - _settings.BlinkMinIntervalSeconds) * _random.NextDouble());

    private float Shape(double t)
    {
        var close = _settings.BlinkCloseSeconds;
        var closed = close + _settings.BlinkClosedSeconds;
        if (t < 0.0 || t >= _settings.BlinkDurationSeconds) return 0f;
        if (t < close) return (float)(t / close);
        if (t < closed) return 1f;
        return (float)(1.0 - ((t - closed) / _settings.BlinkOpenSeconds));
    }
}
