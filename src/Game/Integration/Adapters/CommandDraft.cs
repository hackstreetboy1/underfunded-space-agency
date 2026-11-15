using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;

namespace Usa.Game.Integration.Adapters;

/// <summary>A request that has not been sent: it takes its command id only when it is (<see cref="ToRequest"/>), so drafting spends none.</summary>
public sealed record CommandDraft(ActionId Action, CommandSource Source, CrewId? Assignee, IReadOnlyDictionary<string, CommandArg> Args, bool Confirmed = false)
{
    public CommandRequest ToRequest(CommandId id) => new(id, Source, Action, Assignee, Args, Confirmed);
}
