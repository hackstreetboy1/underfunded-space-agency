namespace Usa.Game.Services.Ai.Contracts;

public sealed record EncyclopediaExcerpt(string Id, string Title, string Text);

/// <summary><c>POST /v1/explain</c>. Question 1-600 characters, at most 6 excerpts.</summary>
public sealed record ExplainRequest(string RequestId, string Question) : IAiRequest
{
    public IReadOnlyList<EncyclopediaExcerpt> Excerpts { get; init; } = [];
}

/// <summary><see cref="Sources"/> lists excerpt ids the answer drew on. Answers explain concepts, never solutions.</summary>
public sealed record ExplainResponse(string Answer)
{
    public IReadOnlyList<string> Sources { get; init; } = [];
}
