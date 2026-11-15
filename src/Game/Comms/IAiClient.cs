using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Comms;

/// <summary>
/// The proxy calls comms makes (implemented over <c>AiGateway</c>). Results never throw; a repeated request id
/// returns the proxy's cached answer, so re-sends are safe. Continuations must complete on the main thread.
/// </summary>
public interface IAiClient
{
    Task<AiResult<SttResponse>> TranscribeAsync(string requestId, ReadOnlyMemory<byte> wav, CancellationToken cancellationToken);

    Task<AiResult<InterpretResponse>> InterpretAsync(InterpretRequest request, CancellationToken cancellationToken);

    Task<AiResult<NarrateResponse>> NarrateAsync(NarrateRequest request, CancellationToken cancellationToken);
}
