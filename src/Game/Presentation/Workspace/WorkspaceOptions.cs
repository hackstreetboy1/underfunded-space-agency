namespace Usa.Game.Presentation.Workspace;

/// <summary>Board layout and the success-likelihood band edges (tuning values, GDD 20).</summary>
public sealed record WorkspaceOptions(BoardLayout Layout, double HighLikelihoodAtLeast = 0.8, double MediumLikelihoodAtLeast = 0.5)
{
    public static WorkspaceOptions Default { get; } = new(BoardLayout.Default);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Layout);
        Layout.Validate();
        if (!(MediumLikelihoodAtLeast > 0 && MediumLikelihoodAtLeast <= HighLikelihoodAtLeast && HighLikelihoodAtLeast <= 1))
            throw new ArgumentException("Likelihood band edges must satisfy 0 < medium <= high <= 1.");
    }
}
