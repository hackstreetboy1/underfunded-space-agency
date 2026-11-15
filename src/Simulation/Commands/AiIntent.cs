using System.Text.Json.Serialization;

namespace Usa.Simulation.Commands;

/// <summary>
/// Mirror of the backend's <c>InterpretResponse</c> (backend/app/schemas/interpret.py), with its
/// snake_case wire names. Untrusted: <see cref="AiResponseValidator"/> checks every field.
/// </summary>
public sealed record AiIntent(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("crew_member")] string? CrewMember = null,
    [property: JsonPropertyName("action_id")] string? ActionId = null,
    [property: JsonPropertyName("params")] IReadOnlyList<AiParamValue>? Params = null,
    [property: JsonPropertyName("fact_refs")] IReadOnlyList<string>? FactRefs = null,
    [property: JsonPropertyName("dialogue_template")] string DialogueTemplate = "",
    [property: JsonPropertyName("clarification_question")] string? ClarificationQuestion = null,
    [property: JsonPropertyName("clarification_options")] IReadOnlyList<string>? ClarificationOptions = null,
    [property: JsonPropertyName("requires_confirmation")] bool RequiresConfirmation = false);

/// <summary>Mirror of the backend's <c>ParamValue</c>.</summary>
public sealed record AiParamValue(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value);
