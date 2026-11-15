using System.Globalization;
using Usa.Simulation.Data;

namespace Usa.Simulation.Forecast;

public static class ForecastConfigLoader
{
    public static ForecastConfig Load(IDataSource source)
    {
        var config = new DefinitionLoader(source).Load<ForecastConfig>(ForecastConfig.RelativePath, ForecastConfig.SchemaTag);
        var errors = new DefinitionErrors();
        Validate(config, ForecastConfig.RelativePath, errors);
        errors.ThrowIfAny();
        return config;
    }

    public static void Validate(ForecastConfig config, string file, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(errors);

        Positive(errors, file, "$.horizonH", config.HorizonH);
        Positive(errors, file, "$.recomputePeriodS", config.RecomputePeriodS);
        Positive(errors, file, "$.roundingH", config.RoundingH);
        InRange(errors, file, "$.minBandFraction", config.MinBandFraction, 0, 10);

        var u = config.Uncertainty;
        InRange(errors, file, "$.uncertainty.fresh", u.Fresh, 0, 0.95);
        InRange(errors, file, "$.uncertainty.stale", u.Stale, 0, 0.95);
        InRange(errors, file, "$.uncertainty.unknown", u.Unknown, 0, 0.95);
        if (!(u.Fresh <= u.Stale && u.Stale <= u.Unknown))
            errors.Add(file, "$.uncertainty", "Uncertainty must not decrease from fresh to stale to unknown.");

        var c = config.Confidence;
        Positive(errors, file, "$.confidence.highMaxRelativeWidth", c.HighMaxRelativeWidth);
        Positive(errors, file, "$.confidence.mediumMaxRelativeWidth", c.MediumMaxRelativeWidth);
        if (!(c.HighMaxRelativeWidth <= c.MediumMaxRelativeWidth))
            errors.Add(file, "$.confidence", "highMaxRelativeWidth must not exceed mediumMaxRelativeWidth.");
    }

    private static void Positive(DefinitionErrors errors, string file, string path, double value)
    {
        if (!(value > 0) || double.IsInfinity(value))
            errors.Add(file, path, string.Create(CultureInfo.InvariantCulture, $"Must be a positive number (found {value})."));
    }

    private static void InRange(DefinitionErrors errors, string file, string path, double value, double min, double max)
    {
        if (!(value >= min && value <= max))
            errors.Add(file, path, string.Create(CultureInfo.InvariantCulture, $"Must be between {min} and {max} (found {value})."));
    }
}
