using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Guidance;

/// <summary>
/// The <c>POST /v1/tip</c> rephrase call (implemented over <c>AiGateway</c>). Results never throw; continuations
/// must complete on the thread that owns the <see cref="TipService"/> (Godot's main thread).
/// </summary>
public interface ITipClient
{
    Task<AiResult<TipResponse>> TipAsync(TipRequest request, CancellationToken cancellationToken);
}
