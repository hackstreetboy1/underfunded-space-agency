using Usa.Simulation.Engineering;

namespace Usa.Game.Presentation.Workspace;

public enum TestState
{
    NotRun,

    /// <summary>The rows describe the board as it is now.</summary>
    Current,

    /// <summary>The board or the spares changed after the test; run it again.</summary>
    Stale,
}

/// <summary>The test panel (UI 4.6).</summary>
/// <param name="ExplanationsShown">difficulty.workspace_explain at the time of the test (Easy true, Normal false).</param>
/// <param name="RunMetS">MET when the test ran (<c>mission.met_s</c>); null when not run or the clock is not served.</param>
/// <param name="RunText">e.g. <c>run 07:06:31</c> beside the result; empty without <paramref name="RunMetS"/>.</param>
public sealed record TestPanel(
    TestState State, bool Passed, bool ExplanationsShown, string Summary, IReadOnlyList<CheckRow> Rows, double? RunMetS = null, string RunText = "")
{
    public static TestPanel NotRun { get; } = new(TestState.NotRun, false, false, "", []);
}

/// <summary>One check of the digital test.</summary>
/// <param name="Mark"><c>✓</c> passed, <c>✗</c> failed, <c>◌</c> not checked (needs a complete circuit).</param>
/// <param name="Word"><c>PASS</c>, <c>FAIL</c> or <c>NOT CHECKED</c> (status is never colour alone).</param>
/// <param name="Code">The check name shown on every difficulty, e.g. <c>connector_compatibility</c>.</param>
/// <param name="Title">Plain check name shown with explanations (<c>Plugs fit</c>), or null when the difficulty hides them.</param>
/// <param name="Explanation">Plain-language reason, or null when the difficulty hides explanations.</param>
public sealed record CheckRow(
    CheckKind Check, CheckStatus Status, CheckReason Reason, string Mark, string Word, string Code, string? Title, string? Explanation);
