using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Advisor.Glue;

/// <summary>The advisor's explain port over the shared proxy client.</summary>
public sealed class AiGatewayExplainClient(AiGateway gateway) : IExplainClient
{
    public Task<AiResult<ExplainResponse>> ExplainAsync(ExplainRequest request, CancellationToken cancellationToken) =>
        gateway.ExplainAsync(request, cancellationToken);
}
