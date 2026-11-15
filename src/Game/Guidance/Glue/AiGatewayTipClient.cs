using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Guidance.Glue;

/// <summary><see cref="ITipClient"/> over the proxy gateway. Calls started on the main thread complete there.</summary>
public sealed class AiGatewayTipClient(AiGateway gateway) : ITipClient
{
    public Task<AiResult<TipResponse>> TipAsync(TipRequest request, CancellationToken cancellationToken) =>
        gateway.TipAsync(request, cancellationToken);
}
