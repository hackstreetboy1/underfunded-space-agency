using Usa.Simulation.Commands;
using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>
/// The command pipeline's view of the crew. A member can work while alive and not critical. The best assignee for a task is
/// the Crew area's choice (<see cref="CrewRoster.BestAvailable"/>); for a glance or report it is the healthiest free member.
/// </summary>
internal sealed class CrewDirectory(CrewRoster roster, CrewTaskRunner runner, TaskCatalog tasks) : ICrewDirectory
{
    private readonly CrewId[] _members = roster.Members.Select(m => m.Id).ToArray();

    public IReadOnlyList<CrewId> Members => _members;

    public TaskId? ActiveTask => runner.Active?.Task.Id;

    public bool IsAlive(CrewId crew) => roster.TryFind(crew, out var member) && member.IsAlive;

    /// <summary>Alive, not critical and in the cabin: a member outside on EVA cannot read gauges or work the panels.</summary>
    public bool CanWork(CrewId crew) =>
        roster.TryFind(crew, out var member) && member.IsAlive && member.Band != HealthBand.Critical && member.Location == CrewLocation.Cabin;

    public CrewId? BestAssignee(TaskId? task)
    {
        if (task is { } id && tasks.TryGet(id, out var definition))
        {
            var best = roster.BestAvailable(definition);
            return best is not null && best.Band != HealthBand.Critical ? best.Id : null;
        }

        CrewMember? healthiest = null;
        foreach (var member in roster.Members)
        {
            if (!member.IsAlive || member.IsBusy || member.Band == HealthBand.Critical) continue;
            if (healthiest is null || member.Health > healthiest.Health) healthiest = member;
        }
        return healthiest?.Id;
    }
}
