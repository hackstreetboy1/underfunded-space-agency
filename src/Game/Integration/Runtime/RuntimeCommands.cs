using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// The one submission path of a composed mission: every request the runtime sends (console bridge, comms, workspace) takes
/// its id from the session's single id provider and reaches <see cref="MissionSession.Submit"/> here, which raises
/// <see cref="Submitted"/> with the request exactly as the session logged it.
/// </summary>
public sealed class RuntimeCommands
{
    private readonly MissionSession _session;
    private readonly Func<CommandId> _nextId;

    /// <param name="nextCommandId">The session's id provider; null uses <see cref="MissionSession.NextCommandId"/>.</param>
    public RuntimeCommands(MissionSession session, Func<CommandId>? nextCommandId = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _nextId = nextCommandId ?? session.NextCommandId;
    }

    /// <summary>Every request sent through this path, with the pipeline's answer.</summary>
    public event Action<CommandRequest, ValidationResult>? Submitted;

    public CommandId NextId() => _nextId();

    /// <summary>Submits a request whose id came from <see cref="NextId"/>.</summary>
    public ValidationResult Submit(CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = _session.Submit(request);
        Submitted?.Invoke(request, result);
        return result;
    }
}
