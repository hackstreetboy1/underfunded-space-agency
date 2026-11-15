using Usa.Game.Services.Ai;
using Usa.Simulation.Commands;
using Usa.Simulation.Guidance;

namespace Usa.Game.Guidance;

/// <summary>How one rephrase request ended, for logs and diagnostics.</summary>
/// <param name="Error">The gateway failure, or null when the proxy answered.</param>
/// <param name="Rejections">Validator findings; empty when accepted or when there was no answer.</param>
/// <param name="Applied">The rephrasing replaced the text on the card (valid and the tip still current).</param>
public sealed record TipRephraseOutcome(
    string TipId,
    HintStage Stage,
    AiError? Error,
    IReadOnlyList<AiRejection> Rejections,
    bool Applied)
{
    public bool Accepted => Error is null && Rejections.Count == 0;
}
