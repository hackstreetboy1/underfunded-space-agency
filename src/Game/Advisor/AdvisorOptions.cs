namespace Usa.Game.Advisor;

/// <param name="Timeout">Overall wait for an answer, retries included; past it the fallback is shown.</param>
/// <param name="DuplicateWindow">The same question asked again within this window is ignored.</param>
public sealed record AdvisorOptions(TimeSpan Timeout, TimeSpan DuplicateWindow)
{
    /// <summary>Backend limit on <c>question</c> (<c>app/schemas/explain.py</c>); longer questions are clipped.</summary>
    public const int MaxQuestionChars = 600;

    public static AdvisorOptions Default { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
}
