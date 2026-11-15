namespace Usa.Game.Presentation.CrewFeed;

/// <summary>States of the comm-feed AnimationTree state machine (MODELING 4.5); one looping clip each.</summary>
public enum CrewFeedState
{
    Idle,
    Listen,
    Talk,
    Work,
    ReadGauge,
    Stressed,
    SpinStrain,
    Impaired,
    GLoad,
}
