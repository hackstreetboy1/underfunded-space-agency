using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;

namespace Usa.Game.Comms;

/// <summary>
/// The slice of the mission session that crew communications needs. The session satisfies it through an adapter,
/// so comms never depends on the session type. Every call is made on the main thread.
/// </summary>
public interface ICommsSessionPort
{
    MissionTime Now { get; }

    /// <summary>The mission phase id, e.g. <c>docked_ops</c>.</summary>
    string Phase { get; }

    /// <summary>Every crew member in id order, dead ones included.</summary>
    IReadOnlyList<CommsCrewStatus> Crew { get; }

    /// <summary>The actions the player may request in <see cref="Phase"/>, in id order.</summary>
    IReadOnlyList<CatalogAction> AllowedActions { get; }

    /// <summary>Facts <paramref name="crew"/> observed or was told, in id order.</summary>
    IReadOnlyList<FactId> FactsKnownBy(CrewId crew);

    bool Knows(CrewId crew, FactId fact);

    /// <summary><c>FactTemplateResolver.Resolve</c> for <paramref name="speaker"/> at <see cref="Now"/>.</summary>
    FactTemplateResult ResolveFacts(string template, CrewId speaker);

    /// <summary>Player-facing task title, e.g. "Cycle OAMS thruster breakers".</summary>
    string DescribeTask(TaskId task);

    /// <summary>
    /// Player-facing words for an order and the target its arguments name, never a raw id, e.g. "Cycle OAMS thruster
    /// breakers T1–T4" or "Arm RCS ring A".
    /// </summary>
    string DescribeCommand(CommandRequest command);

    /// <summary>
    /// <c>CommandProcessor.Submit</c> at <see cref="Now"/>. The orchestrator never calls it while frozen (connection
    /// loss or any app freeze): commands, quick commands and confirmations are queued and submitted in order, with
    /// their original ids, when the freeze ends.
    /// </summary>
    ValidationResult Submit(CommandRequest request);

    /// <summary>
    /// Task lifecycle, recorded crew observations and automatic crew reports, raised in simulation order during the
    /// host's per-frame flush, never re-entrantly from <see cref="Submit"/>. (The orchestrator also copes with a
    /// session that raises them before <see cref="Submit"/> returns, as the test session does.)
    /// </summary>
    event Action<CommsSessionEvent>? EventRaised;
}

/// <param name="HealthPercent">0-100; drives the stress level of narration.</param>
/// <param name="ActiveTask">The task this member is working in the single task slot, if any.</param>
public sealed record CommsCrewStatus(CrewId Id, bool IsAlive, bool IsBusy, double HealthPercent, TaskId? ActiveTask = null);
