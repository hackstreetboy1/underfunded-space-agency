namespace Usa.Simulation.Vehicle.Definitions;

/// <summary>Electrical load priority. Brownout shedding drops the lowest priority (largest value) first.</summary>
public enum LoadPriority
{
    Survival,
    Return,
    Support,
    Discretionary,
}
