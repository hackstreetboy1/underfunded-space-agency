namespace Usa.Simulation.Flight;

public enum FlightPhase
{
    /// <summary>Analytic two-body coast.</summary>
    Coast,

    /// <summary>Engines firing; RK4 at 0.1 s.</summary>
    Powered,

    /// <summary>Below the entry interface; RK4 with drag at 0.1 s.</summary>
    Atmospheric,

    Landed,
}

public static class FlightPhaseNames
{
    /// <summary>The state-path text of a phase.</summary>
    public static string ToStateText(this FlightPhase phase) => phase switch
    {
        FlightPhase.Coast => "coast",
        FlightPhase.Powered => "powered",
        FlightPhase.Atmospheric => "atmospheric",
        FlightPhase.Landed => "landed",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };
}
