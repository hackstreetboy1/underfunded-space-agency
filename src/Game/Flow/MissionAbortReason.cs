namespace Usa.Game.Flow;

/// <summary>Why a flight went back to the main menu without a debrief.</summary>
public enum MissionAbortReason
{
    /// <summary>The flight ended, but its debrief could not be composed.</summary>
    DebriefUnavailable,
}
