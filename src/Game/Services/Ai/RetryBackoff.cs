namespace Usa.Game.Services.Ai;

/// <summary>Jittered exponential backoff between attempts (pure, for tests).</summary>
public static class RetryBackoff
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(2);

    /// <param name="retryNumber">1 for the first retry.</param>
    /// <param name="jitter01">Uniform sample in [0, 1); scales the delay by 0.5-1.5 so clients don't retry in lockstep.</param>
    /// <param name="retryAfter">The proxy's <c>Retry-After</c>, honoured up to <see cref="MaxDelay"/>.</param>
    public static TimeSpan Delay(int retryNumber, double jitter01, TimeSpan? retryAfter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryNumber, 1);
        var exponential = BaseDelay.TotalMilliseconds * Math.Pow(2, retryNumber - 1);
        var jittered = exponential * (0.5 + Math.Clamp(jitter01, 0.0, 1.0));
        var delayMs = Math.Max(jittered, retryAfter?.TotalMilliseconds ?? 0);
        return TimeSpan.FromMilliseconds(Math.Min(delayMs, MaxDelay.TotalMilliseconds));
    }
}
