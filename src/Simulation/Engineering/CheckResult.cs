namespace Usa.Simulation.Engineering;

/// <param name="Findings">What was found, for difficulties with workspace_explain; never empty.</param>
public sealed record CheckResult(CheckKind Check, CheckStatus Status, CheckReason Reason, IReadOnlyList<CheckFinding> Findings)
{
    public bool Passed => Status == CheckStatus.Passed;

    public bool Failed => Status == CheckStatus.Failed;

    /// <summary>The check's name as shown on Normal (snake_case, e.g. <c>connector_compatibility</c>).</summary>
    public string Code => Check switch
    {
        CheckKind.Continuity => "continuity",
        CheckKind.ConnectorCompatibility => "connector_compatibility",
        CheckKind.Protection => "protection",
        CheckKind.Length => "length",
        CheckKind.ShortToGround => "short_to_ground",
        CheckKind.Inventory => "inventory",
        _ => throw new InvalidOperationException($"Unknown check kind {Check}."),
    };
}
