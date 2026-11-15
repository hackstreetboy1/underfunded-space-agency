namespace Usa.Game.Services.Ai;

/// <summary>
/// Per-endpoint call policy, from <c>backend/README.md</c> "Latency budgets and client timeouts".
/// <see cref="ServerBudget"/> is the proxy's own deadline (<c>app/limits.py</c>; past it the proxy answers 504
/// <c>provider_timeout</c>, retryable). <see cref="AttemptTimeout"/> is the client's wait for response headers and
/// is deliberately longer, so the client receives the proxy's typed error instead of racing it.
/// <see cref="BodyTimeout"/> bounds reading the body once headers arrived.
/// </summary>
public sealed record AiEndpoint(string Name, string Path, TimeSpan ServerBudget, TimeSpan AttemptTimeout, int MaxRetries, TimeSpan BodyTimeout)
{
    private static readonly TimeSpan JsonBodyTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Probes are cached for 30 s, so a heartbeat normally answers in milliseconds. A cache miss can take longer;
    /// one slow heartbeat is absorbed by the connection monitor's two-strike rule. No retries for the same reason.
    /// </summary>
    public static AiEndpoint Health { get; } = new("health", "health", TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), 0, JsonBodyTimeout);

    public static AiEndpoint Stt { get; } = new("stt", "v1/stt", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4), 2, JsonBodyTimeout);

    /// <summary>The proxy's 7 s budget covers its model fallback chain (2.5 s + 2.5 s + the rest).</summary>
    public static AiEndpoint Interpret { get; } = new("interpret", "v1/interpret", TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(9), 2, JsonBodyTimeout);

    public static AiEndpoint Narrate { get; } = new("narrate", "v1/narrate", TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(8), 2, JsonBodyTimeout);

    public static AiEndpoint Tip { get; } = new("tip", "v1/tip", TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(8), 2, JsonBodyTimeout);

    public static AiEndpoint Explain { get; } = new("explain", "v1/explain", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), 2, JsonBodyTimeout);

    /// <summary>Timeouts are to first byte; the clip then streams for up to 20 s (<c>STREAM_TOTAL_S</c>).</summary>
    public static AiEndpoint Tts { get; } = new("tts", "v1/tts", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), 2, TimeSpan.FromSeconds(20));

    public int MaxAttempts => MaxRetries + 1;
}
