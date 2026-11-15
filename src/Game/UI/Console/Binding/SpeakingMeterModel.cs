namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// The crew card's speaking indicator (UI.md 3.4: "animated waveform bars while TTS plays"): three bar heights, 0..1 of the
/// meter's height, driven by the speech player's output level for that crew member. The level is smoothed (fast attack,
/// slower release) and each bar sways at its own rate so the bars do not move in lock-step; with no audio (a caption-only
/// line, or speech held by a freeze) they rest low. With Reduced motion the bars hold a fixed shape.
/// </summary>
public sealed class SpeakingMeterModel
{
    public const int BarCount = 3;

    /// <summary>Where every bar rests, so the indicator reads as "speaking" even between words.</summary>
    public const float RestFraction = 0.25f;

    private const double AttackSeconds = 0.04;
    private const double ReleaseSeconds = 0.18;

    private static readonly float[] StaticShape = [0.55f, 1f, 0.75f];
    private static readonly double[] SwayRadiansPerSecond = [9.0, 13.0, 7.0];
    private static readonly double[] SwayPhase = [0.0, 1.7, 3.4];

    private float _envelope;
    private double _seconds;

    /// <summary>The smoothed level, 0..1.</summary>
    public float Envelope => _envelope;

    /// <param name="level">The speech player's output level, 0..1 (values outside are clamped; NaN reads as silence).</param>
    public void Advance(double deltaSeconds, float level)
    {
        if (!(deltaSeconds > 0)) return;
        var target = float.IsNaN(level) ? 0f : Math.Clamp(level, 0f, 1f);
        var timeConstant = target > _envelope ? AttackSeconds : ReleaseSeconds;
        _envelope += (float)((target - _envelope) * (1 - Math.Exp(-deltaSeconds / timeConstant)));
        if (_envelope < 1e-4f) _envelope = 0f;
        _seconds += deltaSeconds;
    }

    /// <summary>Back to silence, for the next line.</summary>
    public void Reset()
    {
        _envelope = 0f;
        _seconds = 0;
    }

    /// <returns>Bar <paramref name="bar"/>'s height as a fraction of the meter's height.</returns>
    public float Height(int bar, bool reducedMotion)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bar);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bar, BarCount);
        if (reducedMotion) return StaticShape[bar];
        var sway = 0.6 + 0.4 * (0.5 + 0.5 * Math.Sin(_seconds * SwayRadiansPerSecond[bar] + SwayPhase[bar]));
        return RestFraction + (1f - RestFraction) * _envelope * (float)sway;
    }
}
