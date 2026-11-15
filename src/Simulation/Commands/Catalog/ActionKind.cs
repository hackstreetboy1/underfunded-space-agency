namespace Usa.Simulation.Commands.Catalog;

/// <summary>How an action is carried out once validated.</summary>
public enum ActionKind
{
    /// <summary>Occupies the single crew task slot (GDD 5.3).</summary>
    CrewTask,

    /// <summary>Ground-commanded; runs without an astronaut, even while the slot is busy.</summary>
    Remote,

    /// <summary>A short instrument read by a crew member; never uses the slot.</summary>
    GlanceRead,

    /// <summary>Session-level commands such as stop, acknowledge or report.</summary>
    Meta,
}
