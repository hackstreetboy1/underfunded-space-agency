using Usa.Simulation.Kernel;

namespace Usa.Simulation.Guidance;

public enum TutorialBeatStatus
{
    Waiting,
    Queued,
    Shown,
    Done,
}

public enum TutorialCloseReason
{
    Completed,
    Dismissed,
    TimedOut,
    Disabled,
    Ended,
}

/// <summary>
/// Show a callout pointing at <paramref name="Anchor"/>: the optional <paramref name="Title"/> above <paramref name="Text"/>, and
/// the order it offers to send (<paramref name="Order"/>) when the step is a typed order.
/// </summary>
public sealed record TutorialCalloutShown(MissionTime Time, string BeatId, string Anchor, string Text, string? Title = null, TutorialOrder? Order = null) : ISimEvent;

public sealed record TutorialCalloutClosed(MissionTime Time, string BeatId, TutorialCloseReason Reason) : ISimEvent;

/// <summary>The guided first run is over; dynamic tips take over from here.</summary>
public sealed record TutorialFinished(MissionTime Time) : ISimEvent;
