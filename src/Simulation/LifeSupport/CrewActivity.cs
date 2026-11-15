namespace Usa.Simulation.LifeSupport;

/// <summary>Metabolic activity level of a crew member; scales O2 use, CO2 output and body heat.</summary>
public enum CrewActivity
{
    Rest,

    /// <summary>A normal mission day including the exercise period (the 0.82 kg O2/day reference).</summary>
    Nominal,

    /// <summary>Sustained physical work such as repairs or wrestling a tumbling cabin.</summary>
    Work,

    Eva,
}
