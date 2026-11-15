using Usa.Game.Presentation.Workspace;
using Usa.Simulation.Engineering;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// <see cref="IRepairReservations"/> for the workspace: the parts each accepted Assign reserved, reported only while the
/// session holds that goal's registration ready (<see cref="RepairWorkspace.IsReady"/>), so an installed goal holds nothing.
/// </summary>
public sealed class RepairReservationTracker(RepairWorkspace workspace) : IRepairReservations
{
    private readonly RepairWorkspace _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    private readonly Dictionary<string, IReadOnlyList<PartQuantity>> _reserved = new(StringComparer.Ordinal);

    /// <summary>Records an accepted Assign's parts; a refused one leaves the goal's previous registration, as the session does.</summary>
    public void Record(RepairSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.Registration.IsValid) _reserved[submission.Assignment.AssemblyId] = submission.Assignment.Task.PartsUsed;
    }

    public IReadOnlyList<PartQuantity> ReservedBy(string goalId)
    {
        ArgumentNullException.ThrowIfNull(goalId);
        return _workspace.IsReady(goalId) && _reserved.TryGetValue(goalId, out var parts) ? parts : [];
    }
}
