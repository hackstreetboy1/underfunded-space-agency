using System.Globalization;
using Usa.Simulation.Data;

namespace Usa.Simulation.Flight;

/// <summary>Ground tracking tuning (<c>data/config/navigation.json</c>, schema <c>navigation/1</c>; X8 stage 2).</summary>
/// <param name="FixAfterTrackS">Coasting radar track inside one station pass needed for a fix.</param>
/// <param name="FixSigma">1σ error of a fresh fix.</param>
/// <param name="GrowthPerOrbit">1σ growth per orbital period since the fix.</param>
/// <param name="BurnExecutionFraction">1σ execution error of a commanded burn, as a fraction of its Δv.</param>
/// <param name="PointingSigmaDeg">1σ pointing error of a burn held by attitude control.</param>
/// <param name="PointingSigmaNoAttitudeDeg">1σ pointing error of a burn without attitude control.</param>
/// <param name="LedgerFraction">1σ relative error of the ground's design dry masses.</param>
/// <param name="StaleAfterS">An estimate with no fix for this long is stale.</param>
/// <param name="ClampSigma">Seeded fix errors never exceed this many σ.</param>
/// <param name="DisplaySigma">Displayed bands and landing arcs are this many σ.</param>
/// <param name="MinBandPx">Displayed bands are never thinner than this.</param>
public sealed record NavigationConfig(
    string Schema,
    double FixAfterTrackS,
    NavSigma FixSigma,
    NavSigma GrowthPerOrbit,
    double BurnExecutionFraction,
    double PointingSigmaDeg,
    double PointingSigmaNoAttitudeDeg,
    double LedgerFraction,
    double StaleAfterS,
    double ClampSigma,
    double DisplaySigma,
    double MinBandPx) : IVersionedDefinition
{
    public const string SchemaTag = "navigation/1";
    public const string RelativePath = "config/navigation.json";

    public static NavigationConfig Load(IDataSource source)
    {
        var config = new DefinitionLoader(source).Load<NavigationConfig>(RelativePath, SchemaTag);
        var errors = new DefinitionErrors();
        Validate(config, RelativePath, errors);
        errors.ThrowIfAny();
        return config;
    }

    public static void Validate(NavigationConfig config, string file, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(errors);
        Positive("$.fixAfterTrackS", config.FixAfterTrackS);
        NotNegative("$.fixSigma.alongM", config.FixSigma.AlongM);
        NotNegative("$.fixSigma.radialM", config.FixSigma.RadialM);
        NotNegative("$.growthPerOrbit.alongM", config.GrowthPerOrbit.AlongM);
        NotNegative("$.growthPerOrbit.radialM", config.GrowthPerOrbit.RadialM);
        NotNegative("$.burnExecutionFraction", config.BurnExecutionFraction);
        NotNegative("$.pointingSigmaDeg", config.PointingSigmaDeg);
        NotNegative("$.pointingSigmaNoAttitudeDeg", config.PointingSigmaNoAttitudeDeg);
        NotNegative("$.ledgerFraction", config.LedgerFraction);
        Positive("$.staleAfterS", config.StaleAfterS);
        Positive("$.clampSigma", config.ClampSigma);
        Positive("$.displaySigma", config.DisplaySigma);
        NotNegative("$.minBandPx", config.MinBandPx);
        if (config.PointingSigmaNoAttitudeDeg < config.PointingSigmaDeg)
            errors.Add(file, "$.pointingSigmaNoAttitudeDeg", "Pointing without attitude control can't be better than with it.");

        void Positive(string path, double value)
        {
            if (!(value > 0) || double.IsInfinity(value))
                errors.Add(file, path, string.Create(CultureInfo.InvariantCulture, $"Must be a positive number (found {value})."));
        }

        void NotNegative(string path, double value)
        {
            if (!(value >= 0) || double.IsInfinity(value))
                errors.Add(file, path, string.Create(CultureInfo.InvariantCulture, $"Must be zero or more (found {value})."));
        }
    }
}

/// <summary>A 1σ error split along-track and radial, in metres.</summary>
public sealed record NavSigma(double AlongM, double RadialM);
