namespace Usa.Game.Presentation.Map.Planning;

public enum PlannerSeverity
{
    Info,
    Caution,

    /// <summary>A preview warning in <c>critical</c>, or a validation issue that blocks Execute.</summary>
    Critical,
}

public enum PlannerMessageKind
{
    NoEngine,
    EngineUnavailable,
    NoDuration,
    DurationClamped,
    FixedDuration,
    StartInPast,
    PreviewUnavailable,
    AttitudeControlUnavailable,
    PropellantLimited,
    PropellantMayRunOut,
    PropellantUnknown,
    EntryTooSteep,
    EntryTooShallow,
    LandingOutsideZones,
    NoLandingInHorizon,
}

/// <summary>A validation issue or preview warning line in the planner panel.</summary>
/// <param name="Blocking">Execute stays disabled while this is present.</param>
public sealed record PlannerMessage(PlannerMessageKind Kind, PlannerSeverity Severity, string Text, bool Blocking = false)
{
    public string ColorToken => Severity switch
    {
        PlannerSeverity.Critical => "critical",
        PlannerSeverity.Caution => "caution",
        _ => "text.mid",
    };
}
