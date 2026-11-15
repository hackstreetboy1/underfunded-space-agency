using Usa.Game.Integration.Adapters;
using Usa.Game.Integration.Runtime;
using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Console.Binding;

/// <summary>Where the console sends the runtime context's events.</summary>
public interface IConsoleRuntimeSink
{
    /// <summary>Stop on a task that is unsafe to interrupt: open the confirmation with the "UNSAFE TO STOP" flag.</summary>
    void ConfirmStop(TaskId task);

    /// <summary>"Ask" and "Request…": place the text in the comms input for the player to review and send.</summary>
    void Prefill(CommsPrefill prefill);

    /// <summary>A console or workspace command was submitted, with the session's answer.</summary>
    void CommandAnswered(CommandRequest request, ValidationResult result);
}

/// <summary>
/// Handles every event of a <see cref="MissionRuntimeContext"/> the console owns, so <see cref="MissionRuntimeContext.UnboundEvents"/>
/// is empty while the console is bound. Dispose to detach.
/// </summary>
public sealed class ConsoleRuntimeEvents : IDisposable
{
    private readonly MissionRuntimeContext _context;
    private readonly IConsoleRuntimeSink _sink;
    private bool _attached;

    public ConsoleRuntimeEvents(MissionRuntimeContext context, IConsoleRuntimeSink sink)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sink);
        _context = context;
        _sink = sink;
        context.StopConfirmationRequested += sink.ConfirmStop;
        context.CommsPrefillRequested += sink.Prefill;
        context.CommandSubmitted += sink.CommandAnswered;
        _attached = true;
    }

    public void Dispose()
    {
        if (!_attached) return;
        _attached = false;
        _context.StopConfirmationRequested -= _sink.ConfirmStop;
        _context.CommsPrefillRequested -= _sink.Prefill;
        _context.CommandSubmitted -= _sink.CommandAnswered;
    }
}
