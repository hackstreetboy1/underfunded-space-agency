using Usa.Game.Presentation.Console.Status;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>Content of the forecast breakdown popover (UI.md 3.2, GDD 6.4).</summary>
/// <param name="ComputedAtText"><c>Computed MET 06:58</c>.</param>
/// <param name="ConfidenceText"><c>Confidence: medium (fuel cell 2 output not checked)</c>.</param>
/// <param name="CoverageText">Plain sentence about the planned return.</param>
public sealed record ForecastBreakdown(
    string Headline,
    string ComputedAtText,
    string ConfidenceText,
    string CoverageText,
    StatusVisual Coverage,
    IReadOnlyList<ForecastBreakdownRow> Rows,
    IReadOnlyList<string> Assumptions)
{
    public bool Equals(ForecastBreakdown? other) =>
        other is not null
        && Headline == other.Headline
        && ComputedAtText == other.ComputedAtText
        && ConfidenceText == other.ConfidenceText
        && CoverageText == other.CoverageText
        && Coverage == other.Coverage
        && Rows.SequenceEqual(other.Rows)
        && Assumptions.SequenceEqual(other.Assumptions, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Headline, ComputedAtText, Rows.Count, Assumptions.Count);
}

/// <summary>One resource in the breakdown.</summary>
/// <param name="RangeText"><c>7.5–9 h</c>, <c>40+ h</c>.</param>
/// <param name="Badge"><c>STALE</c> or <c>UNKNOWN</c> when the input is not fresh; otherwise null.</param>
/// <param name="Limiting">This resource sets the safe autonomy.</param>
public sealed record ForecastBreakdownRow(string Label, string RangeText, string? Badge, bool Limiting);
