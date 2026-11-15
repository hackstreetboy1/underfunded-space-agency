namespace Usa.Simulation.Flight;

/// <summary>Engine output at an instant.</summary>
public readonly record struct ThrustLevel(double ThrustN, double MassFlowKgPerS)
{
    public static readonly ThrustLevel Off = new(0, 0);
}
