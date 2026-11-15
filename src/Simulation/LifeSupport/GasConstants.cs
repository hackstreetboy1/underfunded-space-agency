namespace Usa.Simulation.LifeSupport;

/// <summary>Physical constants for the ideal-gas cabin model.</summary>
internal static class GasConstants
{
    public const double UniversalGasConstantJPerMolK = 8.314462618;
    public const double MolarMassO2KgPerMol = 0.0319988;
    public const double MolarMassCo2KgPerMol = 0.0440095;

    /// <summary>The inert diluent is modelled as nitrogen.</summary>
    public const double MolarMassInertKgPerMol = 0.0280134;

    public const double PaPerMmhg = 133.322387415;
    public const double SecondsPerHour = 3_600;
    public const double SecondsPerDay = 86_400;
}
