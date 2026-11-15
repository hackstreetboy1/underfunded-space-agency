namespace Usa.Simulation.Crew.Health;

/// <summary>What one astronaut is exposed to during a health step.</summary>
/// <param name="AngularRateDps">Total body rotation rate magnitude.</param>
/// <param name="Fatigue">0 (rested) to 1 (exhausted).</param>
/// <param name="Injury">0 (none) to 1 (severe).</param>
public readonly record struct CrewExposure(
    double Ppco2Mmhg,
    double Ppo2Kpa,
    double PressureKpa,
    double TemperatureC,
    double AngularRateDps,
    double GLoad,
    double HydrationDeficitL,
    double NutritionDeficitKcal,
    double Fatigue,
    double Injury)
{
    /// <summary>A comfortable pure-oxygen cabin in orbit; also the reading for inputs that are not simulated.</summary>
    public static CrewExposure Nominal { get; } = new(
        Ppco2Mmhg: 2.0,
        Ppo2Kpa: 34.0,
        PressureKpa: 34.0,
        TemperatureC: 22.0,
        AngularRateDps: 0.0,
        GLoad: 0.0,
        HydrationDeficitL: 0.0,
        NutritionDeficitKcal: 0.0,
        Fatigue: 0.0,
        Injury: 0.0);

    public double InputFor(HealthCause cause) => cause switch
    {
        HealthCause.Ppco2 => Ppco2Mmhg,
        HealthCause.LowPpo2 => Ppo2Kpa,
        HealthCause.LowPressure => PressureKpa,
        HealthCause.HighTemperature or HealthCause.LowTemperature => TemperatureC,
        HealthCause.AngularRate => AngularRateDps,
        HealthCause.GLoad => GLoad,
        HealthCause.Dehydration => HydrationDeficitL,
        HealthCause.Malnutrition => NutritionDeficitKcal,
        HealthCause.Fatigue => Fatigue,
        HealthCause.Injury => Injury,
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null),
    };
}
