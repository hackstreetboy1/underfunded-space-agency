using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Advisor;

/// <summary>The advisor's port to <c>POST /v1/explain</c>; production uses <c>AiGateway.ExplainAsync</c>.</summary>
public interface IExplainClient
{
    /// <summary>Failures come back as <see cref="AiResult{T}"/> values; cancellation may also throw.</summary>
    Task<AiResult<ExplainResponse>> ExplainAsync(ExplainRequest request, CancellationToken cancellationToken);
}
