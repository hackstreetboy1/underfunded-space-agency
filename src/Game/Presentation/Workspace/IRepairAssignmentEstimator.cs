using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Workspace;

/// <summary>
/// Crew candidates for a repair and how each would fare (backed by the Crew area's performance model).
/// The workspace only shows a likelihood band; the probability never reaches the player.
/// </summary>
public interface IRepairAssignmentEstimator
{
    /// <summary>Candidates in display order.</summary>
    IReadOnlyList<AssigneeEstimate> Estimate(RepairTaskSpec task);
}

/// <param name="DurationFactor">Predicted duration as a multiple of <see cref="RepairTaskSpec.DurationS"/> (skill, health, rates).</param>
/// <param name="SuccessProbability">0-1 chance the installation succeeds.</param>
/// <param name="UnavailableReason">Plain-language reason when <paramref name="Available"/> is false (e.g. on EVA); empty otherwise.</param>
public sealed record AssigneeEstimate(
    CrewId Crew,
    string Name,
    bool Available,
    string UnavailableReason,
    double DurationFactor,
    double SuccessProbability);
