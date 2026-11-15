using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Game.Comms;

/// <summary>What <see cref="AiResponseValidator"/> reads (phase, roster, known facts), served live from the session port.</summary>
internal sealed class CommsValidationView(ICommsSessionPort session) : IStateQuery, ICrewDirectory, IKnownFacts
{
    public IReadOnlyList<CrewId> Members => session.Crew.Select(c => c.Id).Order().ToArray();

    public TaskId? ActiveTask
    {
        get
        {
            foreach (var member in session.Crew)
            {
                if (member.ActiveTask is { } task) return task;
            }
            return null;
        }
    }

    public bool TryGet(string path, out StateValue value)
    {
        if (string.Equals(path, CommandValidator.PhasePath, StringComparison.Ordinal))
        {
            value = StateValue.Of(session.Phase);
            return true;
        }
        value = StateValue.None;
        return false;
    }

    public bool IsKnownPath(string path) => string.Equals(path, CommandValidator.PhasePath, StringComparison.Ordinal);

    public bool IsAlive(CrewId crew) => Status(crew)?.IsAlive ?? false;

    public bool CanWork(CrewId crew) => IsAlive(crew);

    /// <summary>Comms never assigns work itself; the command processor picks the assignee.</summary>
    public CrewId? BestAssignee(TaskId? task) => null;

    public bool Knows(CrewId crew, FactId fact) => session.Knows(crew, fact);

    private CommsCrewStatus? Status(CrewId crew)
    {
        foreach (var member in session.Crew)
        {
            if (member.Id == crew) return member;
        }
        return null;
    }
}
