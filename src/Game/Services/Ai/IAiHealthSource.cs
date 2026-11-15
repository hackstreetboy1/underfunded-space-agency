using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Services.Ai;

/// <summary>What <c>ConnectionMonitor</c> needs from the gateway; a seam for tests.</summary>
public interface IAiHealthSource
{
    /// <summary>
    /// Raised for every failed attempt that suggests an outage (<see cref="AiError.IndicatesOutage"/>), on the
    /// synchronization context the source was constructed on (the main thread in the game), never on a thread-pool
    /// thread when one was present.
    /// </summary>
    event Action<AiError>? OutageSuspected;

    Task<AiResult<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default);
}
