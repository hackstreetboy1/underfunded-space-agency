namespace Usa.Game.Services.Voice;

/// <summary>
/// How long a spoken line and its caption last (UI.md: captions show when the line starts and stay 1.5 s after it ends).
/// A line with audio keeps its caption for at least its clip and long enough to read the caption, which may hold more
/// data than the short spoken line.
/// A caption-only line (no TTS, e.g. during an outage) lasts as long as reading it takes, within
/// [<see cref="MinLineSeconds"/>, <see cref="MaxCaptionOnlySeconds"/>].
/// </summary>
public static class CaptionTiming
{
    /// <summary>Comfortable subtitle reading speed (about 180 words a minute).</summary>
    public const double ReadingCharsPerSecond = 15.0;

    public const double MinLineSeconds = 1.5;

    /// <summary>Covers about 900 characters; the proxy's TTS text cap is 1000, so only extreme lines are clipped.</summary>
    public const double MaxCaptionOnlySeconds = 60.0;

    public const double TailSeconds = 1.5;

    /// <summary>How long a caption-only line holds its speaker's channel.</summary>
    public static double CaptionOnlySeconds(string caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        return Math.Clamp(caption.Length / ReadingCharsPerSecond, MinLineSeconds, MaxCaptionOnlySeconds);
    }

    /// <summary>
    /// Seconds the caption stays up from the moment the line starts: the longer of the clip and the time it takes to read
    /// the caption (the spoken line is a short headline while the caption carries every value), never under
    /// <see cref="MinLineSeconds"/>, plus <see cref="TailSeconds"/>.
    /// </summary>
    public static double HoldSeconds(double? clipSeconds, string caption)
    {
        var reading = CaptionOnlySeconds(caption);
        var line = clipSeconds is { } clip && double.IsFinite(clip) && clip > 0
            ? Math.Max(clip, reading)
            : reading;
        return line + TailSeconds;
    }
}
