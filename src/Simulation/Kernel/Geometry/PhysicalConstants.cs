namespace Usa.Simulation.Kernel.Geometry;

/// <summary>Physical constants only. Tunable gameplay values belong in data files.</summary>
public static class PhysicalConstants
{
    /// <summary>Standard gravity, m/s² (rocket equation, g-load).</summary>
    public const double StandardGravity = 9.80665;

    /// <summary>Stefan-Boltzmann constant, W/(m²·K⁴).</summary>
    public const double StefanBoltzmann = 5.670374419e-8;

    public const double ZeroCelsiusInKelvin = 273.15;

    public const double EarthMu = 3.986004418e14;
    public const double EarthRadiusM = 6_371_000;

    /// <summary>Sidereal rotation rate, rad/s.</summary>
    public const double EarthRotationRadPerS = 7.2921159e-5;

    public const double MoonMu = 4.9048695e12;
    public const double MoonRadiusM = 1_737_400;
    public const double MoonOrbitRadiusM = 384_400_000;
}
