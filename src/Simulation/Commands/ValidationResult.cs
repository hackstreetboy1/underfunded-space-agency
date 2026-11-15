namespace Usa.Simulation.Commands;

public enum ValidationStatus
{
    Valid,
    NeedsConfirmation,
    Refused,
    CrewBusy,

    /// <summary>The command id was already executed; nothing happens again.</summary>
    Duplicate,
}

/// <summary>
/// The pipeline's answer to a request. <see cref="ReasonCode"/> explains a refusal; <see cref="Detail"/>
/// adds context such as the offending parameter, the current phase or the task occupying the slot.
/// </summary>
public readonly record struct ValidationResult(ValidationStatus Status, string? ReasonCode, ImpactSummary? Impact, string? Detail = null)
{
    public bool IsValid => Status == ValidationStatus.Valid;

    public static ValidationResult Ok(ImpactSummary? impact = null) => new(ValidationStatus.Valid, null, impact);

    public static ValidationResult NeedsConfirmation(ImpactSummary impact) => new(ValidationStatus.NeedsConfirmation, null, impact);

    public static ValidationResult Refused(string reasonCode, string? detail = null) => new(ValidationStatus.Refused, reasonCode, null, detail);

    public static ValidationResult Busy(string reasonCode, string? detail = null) => new(ValidationStatus.CrewBusy, reasonCode, null, detail);

    public static ValidationResult Duplicate() => new(ValidationStatus.Duplicate, CommandReasons.DuplicateCommand, null);
}
