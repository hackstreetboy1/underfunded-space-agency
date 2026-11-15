namespace Usa.Simulation.Knowledge;

public enum ObservationRejection
{
    None,
    UnknownFact,
    MethodNotAllowed,

    /// <summary>The method cannot confirm a component's condition (glance, instrument, alarms).</summary>
    CannotConfirmComponent,
    ObserverRequired,
    ObserverNotAllowed,
    ObserverDead,
    NoAccess,

    /// <summary>The observation's scope does not include the component it claims to confirm.</summary>
    ScopeDoesNotCover,

    /// <summary>The component it would confirm is outside the revealable set.</summary>
    NotRevealable,
    ValueMissing,
    ValueNotAllowed,
    InvalidConfidence,
}

/// <summary>Outcome of validating an observation; rejections are values, not exceptions.</summary>
public readonly record struct ObservationVerdict(ObservationRejection Rejection, string Detail)
{
    public static ObservationVerdict Accept { get; } = new(ObservationRejection.None, "");

    public bool Accepted => Rejection == ObservationRejection.None;
}
