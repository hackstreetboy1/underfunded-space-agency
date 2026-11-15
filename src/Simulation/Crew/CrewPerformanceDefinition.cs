namespace Usa.Simulation.Crew;

/// <summary>
/// Task duration and error tuning (GDD 5.4). Curves are [x, y] points interpolated linearly.
/// </summary>
public sealed record CrewPerformanceDefinition
{
    /// <summary>Skill level (1-5) to duration multiplier; base durations are written for an expert (skill 5).</summary>
    public required IReadOnlyList<IReadOnlyList<double>> SkillDurationFactor { get; init; }

    /// <summary>Health (0-100) to duration multiplier.</summary>
    public required IReadOnlyList<IReadOnlyList<double>> HealthDurationFactor { get; init; }

    public required RateImpairmentDefinition RateImpairment { get; init; }

    public required TaskErrorModelDefinition Errors { get; init; }
}

/// <summary>Vestibular impairment from spin (GDD 12.5 I5).</summary>
public sealed record RateImpairmentDefinition
{
    public required double ThresholdDps { get; init; }

    public required double DurationMultiplier { get; init; }

    public required double ErrorAdd { get; init; }
}

/// <summary>
/// pError = clamp(complexity × ComplexityWeight − experience × ExperienceWeight + (1 − health/100) × HealthWeight + Base, Min, Max).
/// </summary>
public sealed record TaskErrorModelDefinition
{
    public required double ComplexityWeight { get; init; }

    public required double ExperienceWeight { get; init; }

    public required double HealthWeight { get; init; }

    public required double Base { get; init; }

    public required double Min { get; init; }

    public required double Max { get; init; }
}
