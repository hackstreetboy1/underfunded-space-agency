namespace Usa.Simulation.Vehicle.Definitions;

/// <summary>Who can operate a component: nobody, the crew in the cockpit, or the ground by command.</summary>
public enum ControlAccess
{
    None,
    Crew,
    Remote,
}
