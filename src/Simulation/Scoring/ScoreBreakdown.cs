using Usa.Simulation.Kernel;

namespace Usa.Simulation.Scoring;

/// <summary>The graded result shown in the debrief.</summary>
/// <param name="RawPoints">Sum of the dimension points before the death cap.</param>
/// <param name="CapPoints">100 × survivors / crew (GDD D11).</param>
/// <param name="FinalPoints">min(raw, cap).</param>
/// <param name="ScorePoints">
/// Final points rounded down to whole points for display. The grade is looked up from the unrounded
/// <paramref name="FinalPoints"/>, so the shown score never sits in a higher band than the grade.
/// </param>
/// <param name="BestDecisions">The highest-weighted good decisions, best first.</param>
/// <param name="LargestAvoidableLoss">The avoidable loss that cost the most points, if any.</param>
public sealed record ScoreBreakdown(
    IReadOnlyList<DimensionScore> Dimensions,
    double RawPoints,
    double CapPoints,
    double FinalPoints,
    int ScorePoints,
    Grade Grade,
    string GradeLabel,
    SPlusResult SPlus,
    IReadOnlyList<ObjectiveResult> Objectives,
    IReadOnlyList<DebriefItem> BestDecisions,
    DebriefItem? LargestAvoidableLoss)
{
    public DimensionScore Dimension(ScoreDimension dimension)
    {
        foreach (var score in Dimensions)
        {
            if (score.Dimension == dimension) return score;
        }
        throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension not scored.");
    }
}

/// <param name="WeightPoints">The dimension's maximum points.</param>
/// <param name="Lines">Per-criterion points; each key is a debrief explanation key.</param>
/// <param name="Notes">Further "why" lines, e.g. which objectives left the denominator.</param>
public sealed record DimensionScore(
    ScoreDimension Dimension,
    double WeightPoints,
    double Points,
    IReadOnlyList<ScoreLine> Lines,
    IReadOnlyList<ScoreNote> Notes);

/// <param name="Key">Explanation key, e.g. <c>crew.survival</c>.</param>
/// <param name="Subject">What the line is about (objective or crew id) when the key is not unique.</param>
public sealed record ScoreLine(string Key, string? Subject, double Points, double MaxPoints);

public sealed record ScoreNote(string Key, string? Subject, double? Value);

/// <param name="CountedInDenominator">False when the objective was lost for scenario reasons (fairness rule).</param>
/// <param name="FinalMetS">MET of the last feasibility transition, if any.</param>
public sealed record ObjectiveResult(
    ObjectiveId Id,
    ObjectiveFinalState FinalState,
    ObjectiveLossCause? Cause,
    bool CountedInDenominator,
    double Points,
    double MaxPoints,
    double? FinalMetS);

/// <param name="Magnitude">Weight for good decisions; points lost for avoidable losses.</param>
public sealed record DebriefItem(string Key, string? Subject, double Magnitude, double? MetS);

/// <param name="Path">The path that qualified, or <see cref="SPlusPath.None"/>.</param>
/// <param name="Reasons">Explanation keys: why S+ was achieved or exactly what blocked it.</param>
public sealed record SPlusResult(bool Achieved, SPlusPath Path, IReadOnlyList<ScoreNote> Reasons);
