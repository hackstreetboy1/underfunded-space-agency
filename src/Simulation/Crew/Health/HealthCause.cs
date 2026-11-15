namespace Usa.Simulation.Crew.Health;

/// <summary>Hidden causal inputs that drain health (GDD 5.5). Each has one damage curve in <c>config/health.json</c>.</summary>
public enum HealthCause
{
    Ppco2,
    LowPpo2,
    LowPressure,
    HighTemperature,
    LowTemperature,
    AngularRate,
    GLoad,
    Dehydration,
    Malnutrition,
    Fatigue,
    Injury,
}
