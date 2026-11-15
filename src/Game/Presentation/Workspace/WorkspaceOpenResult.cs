namespace Usa.Game.Presentation.Workspace;

public enum WorkspaceOpenResult
{
    Opened,
    UnknownGoal,

    /// <summary>No sink candidate's condition holds: nothing needs this repair.</summary>
    NothingToRepair,

    /// <summary>A design to restore doesn't fit the goal, its resolved sink or the catalog.</summary>
    DesignMismatch,
}
