using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <param name="Crew">The reporting crew member for a status report; null otherwise.</param>
/// <param name="Args">The parsed arguments in name order (e.g. a repair design); empty when the action has none.</param>
public sealed record MetaCommand(CommandId Origin, MetaCommandKind Kind, CrewId? Crew, MissionTime Now, IReadOnlyDictionary<string, CommandArg>? Args = null)
{
    public IReadOnlyDictionary<string, CommandArg> Arguments => Args ?? CommandRequest.NoArgs;
}

/// <summary>Runs stop_task, acknowledge_alarms, status_report and register_repair_design. Implemented by the session.</summary>
public interface IMetaCommandExecutor
{
    /// <summary>
    /// Reads state only and returns a refusal reason code, or null when <see cref="Execute"/> will succeed.
    /// Called during validation, before any confirmation; e.g. a repair design that doesn't parse or validate.
    /// </summary>
    string? Check(MetaCommand command) => null;

    void Execute(MetaCommand command);
}
