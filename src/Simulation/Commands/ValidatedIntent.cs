using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>Intent kinds the backend may return; <see cref="Unknown"/> is never accepted.</summary>
public enum AiIntentKind
{
    Command,
    Question,
    Clarify,
    RecommendationRequest,
    Chat,
    Unknown,
}

/// <summary>An AI interpretation that passed every check and may be acted on.</summary>
/// <param name="Speaker">
/// The crew member who answers and who knows every fact in <c>FactRefs</c>. A whole-crew answer still has one
/// speaker (the first living member in roster order who knows the facts); null only when nobody is alive.
/// </param>
/// <param name="Command">For <see cref="AiIntentKind.Command"/>: the request to submit (never pre-confirmed).</param>
/// <param name="ModelRequestedConfirmation">Advisory only; the catalog's impact decides confirmation.</param>
public sealed record ValidatedIntent(
    AiIntentKind Kind,
    CrewId? Speaker,
    CommandRequest? Command,
    IReadOnlyList<FactId> FactRefs,
    string DialogueTemplate,
    string? ClarificationQuestion,
    IReadOnlyList<string> ClarificationOptions,
    bool ModelRequestedConfirmation);
