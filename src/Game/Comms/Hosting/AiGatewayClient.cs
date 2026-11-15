using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Comms.Hosting;

/// <summary><see cref="IAiClient"/> over the proxy gateway. Calls started on the main thread complete there.</summary>
public sealed class AiGatewayClient(AiGateway gateway) : IAiClient
{
    public Task<AiResult<SttResponse>> TranscribeAsync(string requestId, ReadOnlyMemory<byte> wav, CancellationToken cancellationToken) =>
        gateway.TranscribeAsync(requestId, wav, cancellationToken);

    public Task<AiResult<InterpretResponse>> InterpretAsync(InterpretRequest request, CancellationToken cancellationToken) =>
        gateway.InterpretAsync(request, cancellationToken);

    public Task<AiResult<NarrateResponse>> NarrateAsync(NarrateRequest request, CancellationToken cancellationToken) =>
        gateway.NarrateAsync(request, cancellationToken);
}
