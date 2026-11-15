namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>A component's condition as the player knows it (GDD 6.1; <see cref="Suspected"/> exists only in knowledge).</summary>
public enum ComponentCondition
{
    Unknown,
    Nominal,
    Degraded,
    Failed,
    Isolated,
    Suspected,
}
