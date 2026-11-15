using Usa.Simulation.Data;

namespace Usa.Simulation.Forecast;

/// <summary>Survival forecast tuning (<c>data/config/forecast.json</c>, schema <c>forecast/1</c>).</summary>
/// <param name="HorizonH">Projection limit; anything lasting longer is reported as "beyond the horizon".</param>
/// <param name="RecomputePeriodS">How often the host recomputes when no input changed.</param>
/// <param name="MinBandFraction">The range is always at least this wide relative to its low end.</param>
/// <param name="RoundingH">The low end rounds down and the high end up to this step (0.25 → "7.5-9 h").</param>
public sealed record ForecastConfig(
    string Schema,
    double HorizonH,
    double RecomputePeriodS,
    KnowledgeUncertainty Uncertainty,
    double MinBandFraction,
    double RoundingH,
    ConfidenceThresholds Confidence) : IVersionedDefinition
{
    public const string SchemaTag = "forecast/1";
    public const string RelativePath = "config/forecast.json";

    public double UncertaintyOf(InputKnowledge knowledge) => knowledge switch
    {
        InputKnowledge.Fresh => Uncertainty.Fresh,
        InputKnowledge.Stale => Uncertainty.Stale,
        _ => Uncertainty.Unknown,
    };
}

/// <summary>Relative uncertainty applied to an input (quantity or rate) by how well the ground knows it.</summary>
public sealed record KnowledgeUncertainty(double Fresh, double Stale, double Unknown);

/// <summary>Range width (high − low) / low above which confidence drops.</summary>
public sealed record ConfidenceThresholds(double HighMaxRelativeWidth, double MediumMaxRelativeWidth);
