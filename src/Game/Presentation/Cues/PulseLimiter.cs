namespace Usa.Game.Presentation.Cues;

/// <summary>Token bucket: up to <c>burst</c> events back to back, then at most <c>ratePerSecond</c> sustained.</summary>
public sealed class PulseLimiter
{
    private readonly int _burst;
    private double _tokens;
    private double _lastSeconds;
    private bool _started;

    public PulseLimiter(int burst)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(burst, 1);
        _burst = burst;
        _tokens = burst;
    }

    /// <returns>How many of <paramref name="requested"/> events may pass now.</returns>
    public int Take(int requested, double nowSeconds, double ratePerSecond)
    {
        if (_started) _tokens = Math.Min(_burst, _tokens + (Math.Max(0.0, nowSeconds - _lastSeconds) * ratePerSecond));
        _started = true;
        _lastSeconds = nowSeconds;
        if (requested <= 0) return 0;
        var granted = Math.Min(requested, (int)Math.Floor(_tokens));
        _tokens -= granted;
        return granted;
    }
}
