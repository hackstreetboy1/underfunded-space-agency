namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>Phase chip in the header (UI.md 3.1): one member per mission phase id, each with its own word.</summary>
public enum MissionPhase
{
    DockedOps,
    Emergency,
    ReturnPrep,
    Entry,
    Recovery,

    /// <summary><c>docking_ops</c>: the approach before the hard dock.</summary>
    DockingOps,

    /// <summary><c>ended</c>: splashdown recovered or the crew lost; the debrief follows.</summary>
    Ended,
}
