using System.Text.Json.Serialization;

namespace Usa.Game.Services.Ai.Contracts;

public enum StressLevel
{
    [JsonStringEnumMemberName("calm")] Calm,
    [JsonStringEnumMemberName("tense")] Tense,
    [JsonStringEnumMemberName("critical")] Critical,
}

/// <summary>How a crew member talks (GDD 5.7), sent from <c>data/crew/crew.json</c>. At most 5 sample lines.</summary>
public sealed record CrewPersona(string Name)
{
    public string Role { get; init; } = "";

    public string Personality { get; init; } = "";

    public string Cadence { get; init; } = "";

    public string Warmth { get; init; } = "";

    public string Humor { get; init; } = "";

    public string StressExpression { get; init; } = "";

    public IReadOnlyList<string> SampleLines { get; init; } = [];
}

/// <summary>An approved fact: id and label plus a qualitative band, never a numeric value.</summary>
public sealed record NarrateFact(string Id, string Label)
{
    public string? Band { get; init; }
}

/// <summary><c>POST /v1/narrate</c>. At most 16 facts.</summary>
public sealed record NarrateRequest(string RequestId, string CrewPersonaId, string Event) : IAiRequest
{
    public CrewPersona? Persona { get; init; }

    public StressLevel Stress { get; init; } = StressLevel.Calm;

    public IReadOnlyList<NarrateFact> Facts { get; init; } = [];
}

/// <summary><see cref="DialogueTemplate"/> carries <c>&lt;FACT:id&gt;</c> tokens that the client resolves.</summary>
public sealed record NarrateResponse(string DialogueTemplate)
{
    public IReadOnlyList<string> FactRefs { get; init; } = [];
}
