using Usa.Simulation.Engineering;

namespace Usa.Game.Presentation.Workspace;

public enum RateAxis
{
    Roll,
    Pitch,
    Yaw,

    /// <summary>The total angular rate.</summary>
    Total,
}

/// <summary>
/// An attitude-rate precondition of the install task (task catalog, e.g. <c>abs(@attitude.tandem.rate_roll_dps) &lt; 10</c>).
/// The workspace checks it against what the ground knows, never the true attitude.
/// </summary>
/// <param name="FactId">Knowledge fact holding the ground's reading of the rate, e.g. <c>tandem.rate.roll_dps</c>.</param>
public sealed record RateRequirement(RateAxis Axis, double LimitDps, string FactId);

/// <summary>The install task's preconditions the ground can check before assigning (backed by the task catalog).</summary>
public interface IRepairRequirementSource
{
    IReadOnlyList<RateRequirement> RateLimits(RepairTaskSpec task);
}

public enum RequirementStatus
{
    /// <summary>The ground has no reading.</summary>
    Unknown,
    Met,
    NotMet,

    /// <summary>The last reading is older than the fact's stale time; shown with its MET, not judged.</summary>
    Stale,
}

/// <summary>One precondition line of the Assign step, judged on knowledge.</summary>
/// <param name="KnownDps">Magnitude of the ground's reading; null when unknown.</param>
/// <param name="KnownAtMetS">MET of that reading; null when unknown.</param>
/// <param name="Text">e.g. <c>roll &lt; 10 °/s (now 0.4, OK)</c>.</param>
public sealed record AssignRequirement(RateRequirement Requirement, RequirementStatus Status, double? KnownDps, double? KnownAtMetS, string Text);
