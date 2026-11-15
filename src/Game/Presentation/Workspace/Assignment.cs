using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Workspace;

public enum LikelihoodBand
{
    Low,
    Medium,
    High,
}

/// <summary>The Assign step: who can install the tested design. Never shows an exact success percentage.</summary>
/// <param name="Requirements">The task's rate preconditions judged on knowledge; empty without a requirement source.</param>
/// <param name="RequirementsText">
/// The picker's requirement line (Figma G5-01b): <c>Complexity 3 · needs roll &lt; 10 °/s (now 0.4, OK) · uses inline fuse, …</c>.
/// </param>
public sealed record AssignmentPreview(
    string GoalId,
    string GoalName,
    RepairTaskSpec Task,
    IReadOnlyList<AssigneeOption> Options,
    IReadOnlyList<AssignRequirement> Requirements,
    string RequirementsText);

/// <param name="PredictedDurationText">e.g. <c>6 min</c>.</param>
/// <param name="LikelihoodText"><c>High</c>, <c>Medium</c> or <c>Low</c>.</param>
public sealed record AssigneeOption(
    CrewId Crew,
    string Name,
    bool Available,
    string UnavailableReason,
    double PredictedDurationS,
    string PredictedDurationText,
    LikelihoodBand Likelihood,
    string LikelihoodText);

/// <summary>What the install_repair command needs: the tested design and who installs it.</summary>
/// <param name="AssemblyId">Identifier argument for <c>install_repair</c>; the goal id.</param>
public sealed record RepairAssignment(string AssemblyId, AssemblyDesign Design, CrewId Assignee, RepairTaskSpec Task);

public enum AssignProblem
{
    None,
    NotOpen,

    /// <summary>No passing test, or the board changed since it.</summary>
    NotTested,
    AlreadyAssigned,
    UnknownAssignee,
    AssigneeUnavailable,
}

public sealed record AssignmentResult(AssignProblem Problem, RepairAssignment? Assignment)
{
    public bool Succeeded => Problem == AssignProblem.None;
}
