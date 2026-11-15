using System.Text.Json.Serialization;

namespace Usa.Game.Services.Ai.Contracts;

public enum TipStage
{
    [JsonStringEnumMemberName("nudge")] Nudge,
    [JsonStringEnumMemberName("investigate")] Investigate,
    [JsonStringEnumMemberName("connect")] Connect,
    [JsonStringEnumMemberName("recommend")] Recommend,
    [JsonStringEnumMemberName("direct")] Direct,
}

/// <summary>Authored hint text from the mission's hint tree; the fallback if rephrasing fails.</summary>
public sealed record HintCandidate(string TipId, TipStage Stage, string Text)
{
    public IReadOnlyList<string> FactRefs { get; init; } = [];

    public IReadOnlyList<string> AllowedActionRefs { get; init; } = [];
}

public sealed record KnownFact(string Id, string Label);

/// <summary><c>POST /v1/tip</c>. 1-8 candidates, at most 32 known facts.</summary>
public sealed record TipRequest(string RequestId, TipStage Stage, IReadOnlyList<HintCandidate> Candidates) : IAiRequest
{
    public IReadOnlyList<KnownFact> KnownFacts { get; init; } = [];
}

public sealed record TipResponse(string TipId, TipStage Stage, string Text)
{
    public IReadOnlyList<string> FactRefs { get; init; } = [];

    public IReadOnlyList<string> AllowedActionRefs { get; init; } = [];
}
