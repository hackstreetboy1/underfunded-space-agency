using System.Globalization;
using System.Text.Json;
using Usa.Simulation.Data;

namespace Usa.Simulation.Crew.Health;

/// <summary>Validated health tuning: one damage curve per <see cref="HealthCause"/>, bands and recovery.</summary>
public sealed class HealthConfig
{
    public const string DefaultPath = "config/health.json";
    public const string SchemaTag = "health/1";
    public const int CauseCount = (int)HealthCause.Injury + 1;

    private readonly PiecewiseLinearCurve[] _curves;
    private readonly double[] _sustainS;
    private readonly string[] _labels;

    private HealthConfig(HealthDefinition definition, PiecewiseLinearCurve[] curves, double[] sustainS, string[] labels)
    {
        LaunchHealth = definition.LaunchHealth;
        Bands = definition.Bands;
        RecoveryPerS = definition.RecoveryPerS;
        ComplicationThreshold = definition.ComplicationThreshold;
        ExposurePaths = definition.ExposurePaths;
        _curves = curves;
        _sustainS = sustainS;
        _labels = labels;
    }

    public double LaunchHealth { get; }

    public HealthBandsDefinition Bands { get; }

    public double RecoveryPerS { get; }

    public double ComplicationThreshold { get; }

    public CrewExposurePaths ExposurePaths { get; }

    public double DamagePerS(HealthCause cause, double input) => _curves[(int)cause].Evaluate(input);

    public double SustainS(HealthCause cause) => _sustainS[(int)cause];

    public string Label(HealthCause cause) => _labels[(int)cause];

    public HealthBand BandFor(double health)
    {
        if (health <= 0) return HealthBand.Deceased;
        if (health >= Bands.StableMin) return HealthBand.Stable;
        if (health >= Bands.StressedMin) return HealthBand.Stressed;
        return health >= Bands.ImpairedMin ? HealthBand.Impaired : HealthBand.Critical;
    }

    public static HealthConfig Load(DefinitionLoader loader, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return Create(loader.Load<HealthDefinition>(path, SchemaTag), path);
    }

    /// <exception cref="DefinitionException">Every problem in the file, reported together.</exception>
    public static HealthConfig Create(HealthDefinition definition, string file)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new DefinitionErrors();

        if (!(definition.LaunchHealth is > 0 and <= 100))
            errors.Add(file, "$.launchHealth", "Launch health must be in (0, 100].");
        var bands = definition.Bands;
        if (!(bands.ImpairedMin > 0 && bands.StressedMin > bands.ImpairedMin && bands.StableMin > bands.StressedMin && bands.StableMin <= 100))
            errors.Add(file, "$.bands", "Bands must satisfy 0 < impairedMin < stressedMin < stableMin <= 100.");
        if (!(definition.RecoveryPerS >= 0 && double.IsFinite(definition.RecoveryPerS)))
            errors.Add(file, "$.recoveryPerS", "Recovery rate must be finite and non-negative.");
        if (!(definition.ComplicationThreshold >= 0))
            errors.Add(file, "$.complicationThreshold", "Complication threshold must be non-negative.");

        var curves = new PiecewiseLinearCurve?[CauseCount];
        var sustainS = new double[CauseCount];
        var labels = new string?[CauseCount];
        for (var i = 0; i < definition.Causes.Count; i++)
        {
            var cause = definition.Causes[i];
            var path = string.Create(CultureInfo.InvariantCulture, $"$.causes[{i}]");
            var index = (int)cause.Cause;
            if (labels[index] is not null)
            {
                errors.Add(file, path, $"Cause '{SnakeName(cause.Cause)}' is defined twice.");
                continue;
            }
            labels[index] = cause.Label;
            if (string.IsNullOrWhiteSpace(cause.Label)) errors.Add(file, path + ".label", "Label is empty.");
            if (!(cause.SustainS >= 0)) errors.Add(file, path + ".sustainS", "Sustain time must be non-negative.");
            sustainS[index] = cause.SustainS;
            curves[index] = PiecewiseLinearCurve.Create(cause.DamagePerS, file, path + ".damagePerS", errors, minY: 0);
        }

        for (var i = 0; i < CauseCount; i++)
        {
            if (labels[i] is null) errors.Add(file, "$.causes", $"Cause '{SnakeName((HealthCause)i)}' has no curve.");
        }

        errors.ThrowIfAny();
        return new HealthConfig(definition, curves!, sustainS, labels!);
    }

    internal static string SnakeName(HealthCause cause) => JsonNamingPolicy.SnakeCaseLower.ConvertName(cause.ToString());
}
