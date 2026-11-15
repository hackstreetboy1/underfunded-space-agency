namespace Usa.Simulation.Commands;

/// <summary>One reason an AI response was discarded.</summary>
public sealed record AiRejection(string Code, string? Detail = null)
{
    public override string ToString() => Detail is null ? Code : $"{Code}: {Detail}";
}

/// <summary>Either an accepted <see cref="Intent"/> or the rejections; on rejection the caller uses the deterministic template.</summary>
public sealed record AiValidationResult(ValidatedIntent? Intent, IReadOnlyList<AiRejection> Rejections)
{
    public bool IsAccepted => Intent is not null;
}
