namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>Who can operate a control (GDD 12.3).</summary>
public enum ControlAuthority
{
    /// <summary>The ground commands it directly through the command pipeline.</summary>
    Remote,

    /// <summary>Cockpit or physical work: the ground can only ask the crew.</summary>
    CrewOnly,
}
