using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Usa.Game.Services.Ai.Contracts;

[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Members mirror the wire literals of ActionParam.type.")]
public enum ActionParamType
{
    [JsonStringEnumMemberName("string")] String,
    [JsonStringEnumMemberName("number")] Number,
    [JsonStringEnumMemberName("integer")] Integer,
    [JsonStringEnumMemberName("boolean")] Boolean,
    [JsonStringEnumMemberName("enum")] Enum,
}

public enum ActionImpact
{
    [JsonStringEnumMemberName("low")] Low,
    [JsonStringEnumMemberName("high")] High,
    [JsonStringEnumMemberName("irreversible")] Irreversible,
}

public enum InterpretKind
{
    [JsonStringEnumMemberName("command")] Command,
    [JsonStringEnumMemberName("question")] Question,
    [JsonStringEnumMemberName("clarify")] Clarify,
    [JsonStringEnumMemberName("recommendation_request")] RecommendationRequest,
    [JsonStringEnumMemberName("chat")] Chat,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

public sealed record ActionParam(string Name, ActionParamType Type)
{
    [JsonPropertyName("enum")]
    public IReadOnlyList<string> EnumValues { get; init; } = [];
}

public sealed record ActionSpec(string Id, string Description, ActionImpact Impact)
{
    public IReadOnlyList<ActionParam> Params { get; init; } = [];
}

/// <summary>
/// <c>POST /v1/interpret</c>. <see cref="Crew"/> and <see cref="Facts"/> are free-form string maps on the
/// proxy side; <see cref="Addressee"/> is required but may be null. <c>player_text</c> is at most 600 characters.
/// </summary>
public sealed record InterpretRequest(
    string RequestId,
    string PlayerText,
    string? Addressee,
    string MissionPhase,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Crew,
    IReadOnlyList<ActionSpec> Actions,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Facts) : IAiRequest
{
    /// <summary>At most 8 lines.</summary>
    public IReadOnlyList<string> RecentTranscript { get; init; } = [];

    /// <summary>Crew offers CAPCOM has not answered yet, newest last; at most 4.</summary>
    public IReadOnlyList<PendingOffer> PendingOffers { get; init; } = [];

    /// <summary>The crew's open clarifying question, if any.</summary>
    public PendingClarification? PendingClarification { get; init; }

    /// <summary>How each crew member talks and how stressed they are, so replies sound like them; at most 4.</summary>
    public IReadOnlyList<InterpretPersona> Personas { get; init; } = [];

    /// <summary>
    /// Gauges the crew can read right now (id and label, never a value); at most 96. A reply that names one with a
    /// <c>&lt;FACT:id&gt;</c> token makes the speaker read it before the line is spoken.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadableFacts { get; init; } = [];
}

/// <summary>A crew member's voice for <c>/v1/interpret</c>: their <see cref="CrewPersona"/> plus id and current stress.</summary>
public sealed record InterpretPersona(string Id, string Name)
{
    public string Role { get; init; } = "";

    public string Personality { get; init; } = "";

    public string Cadence { get; init; } = "";

    public string Warmth { get; init; } = "";

    public string Humor { get; init; } = "";

    public string StressExpression { get; init; } = "";

    public IReadOnlyList<string> SampleLines { get; init; } = [];

    public StressLevel Stress { get; init; } = StressLevel.Calm;
}

public sealed record ParamValue(string Name, string Value);

/// <summary>An open crew offer: what "yes" or "do it" would accept. Label at most 160 characters.</summary>
public sealed record PendingOffer(string Speaker, string Label, string ActionId)
{
    public IReadOnlyList<ParamValue> Params { get; init; } = [];
}

/// <summary>The crew's open clarifying question (at most 300 characters) and its quick replies (at most 4).</summary>
public sealed record PendingClarification(string Speaker, string Question)
{
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>Untrusted model output: validate with <c>AiResponseValidator</c> before anything reaches the simulation.</summary>
public sealed record InterpretResponse(InterpretKind Kind)
{
    public string? CrewMember { get; init; }

    public string? ActionId { get; init; }

    public IReadOnlyList<ParamValue> Params { get; init; } = [];

    public IReadOnlyList<string> FactRefs { get; init; } = [];

    public string DialogueTemplate { get; init; } = "";

    public string? ClarificationQuestion { get; init; }

    public IReadOnlyList<string> ClarificationOptions { get; init; } = [];

    public bool RequiresConfirmation { get; init; }
}
