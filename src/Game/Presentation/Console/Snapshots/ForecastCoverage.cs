namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>Whether the safe-autonomy range covers the planned return (mirrors the forecast's <c>PlanCoverage</c>).</summary>
public enum ForecastCoverage
{
    NoPlanEnd,
    Covered,
    AtRisk,
    NotCovered,
}
