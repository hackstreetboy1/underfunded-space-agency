namespace Usa.Game.Content;

/// <summary>Keys of <see cref="DebriefDefinition.Comparison"/> that <see cref="DebriefComposer"/> uses.</summary>
public static class DebriefComparisonKeys
{
    public const string Intro = "intro";
    public const string DockingCompleted = "docking.completed";
    public const string DockingNotCompleted = "docking.not_completed";
    public const string Undocked = "undock.undocked";

    /// <summary>Undocked after the cause had been identified (<see cref="DebriefFlightFacts.CauseIdentified"/> at or before undocking).</summary>
    public const string UndockedCauseKnown = "undock.undocked_cause_known";

    public const string StayedDocked = "undock.stayed_docked";
    public const string PeakRate = "peakRate";
    public const string CauseFound = "cause.found";
    public const string CauseNotFound = "cause.not_found";
    public const string CauseMethodPrefix = "causeMethod.";
    public const string PathA = "path.a";
    public const string PathB = "path.b";
    public const string EvaCompleted = "eva.completed";
    public const string EvaNotCompleted = "eva.not_completed";
    public const string Landing = "landing";
    public const string RecoveryWait = "recoveryWait";
    public const string CrewLost = "crewLost";
    public const string Outro = "outro";

    /// <summary>The wording of a <see cref="CauseEvidence.Method"/>; the debrief's own cause evidence decides which are needed.</summary>
    public static string CauseMethod(string method) => CauseMethodPrefix + method;

    /// <summary>Every key every debrief needs; the <see cref="CauseMethod"/> keys depend on the mission's cause evidence.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Intro, DockingCompleted, DockingNotCompleted, Undocked, UndockedCauseKnown, StayedDocked, PeakRate, CauseFound, CauseNotFound,
        PathA, PathB, EvaCompleted, EvaNotCompleted, Landing, RecoveryWait, CrewLost, Outro,
    ];
}
