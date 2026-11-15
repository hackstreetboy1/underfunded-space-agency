using Usa.Simulation.Data;

namespace Usa.Simulation.Crew.Health;

/// <summary>Root of <c>config/health.json</c> (schema <c>health/1</c>).</summary>
public sealed record HealthDefinition : IVersionedDefinition
{
    public required string Schema { get; init; }

    public required double LaunchHealth { get; init; }

    public required HealthBandsDefinition Bands { get; init; }

    /// <summary>Health points regained per second while every exposure is nominal, up to launch health.</summary>
    public required double RecoveryPerS { get; init; }

    /// <summary>Mission-caused damage above this many points counts as a complication (GDD 10.3).</summary>
    public required double ComplicationThreshold { get; init; }

    public required IReadOnlyList<HealthCauseDefinition> Causes { get; init; }

    public CrewExposurePaths ExposurePaths { get; init; } = new();
}

/// <summary>Lower bounds of the bands; Critical is anything above zero, Deceased is zero.</summary>
public sealed record HealthBandsDefinition
{
    public required double StableMin { get; init; }

    public required double StressedMin { get; init; }

    public required double ImpairedMin { get; init; }
}

/// <summary>Damage per second as a piecewise-linear function of the cause's exposure input.</summary>
public sealed record HealthCauseDefinition
{
    public required HealthCause Cause { get; init; }

    /// <summary>Plain-language cause for the debrief.</summary>
    public required string Label { get; init; }

    /// <summary>[input, damage per second] points.</summary>
    public required IReadOnlyList<IReadOnlyList<double>> DamagePerS { get; init; }

    /// <summary>The exposure must stay harmful this long before damage starts (e.g. sustained g-load).</summary>
    public double SustainS { get; init; }
}

/// <summary>
/// State paths that feed <see cref="StateExposureSource"/>. "{crew}" is replaced by the crew id;
/// an empty path means the input is not simulated and reads as nominal.
/// </summary>
public sealed record CrewExposurePaths
{
    public string Ppco2Mmhg { get; init; } = "";

    public string Ppo2Kpa { get; init; } = "";

    public string PressureKpa { get; init; } = "";

    public string TemperatureC { get; init; } = "";

    public string AngularRateDps { get; init; } = "";

    public string GLoad { get; init; } = "";

    public string HydrationDeficitL { get; init; } = "";

    public string NutritionDeficitKcal { get; init; } = "";

    public string Fatigue { get; init; } = "";

    public string Injury { get; init; } = "";
}
