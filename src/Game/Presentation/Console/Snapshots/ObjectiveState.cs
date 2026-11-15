namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>Objective feasibility as the header summary shows it (GDD 12.8).</summary>
public enum ObjectiveState
{
    Open,
    Completed,
    UnacceptableRisk,
    Impossible,
    Abandoned,
}
