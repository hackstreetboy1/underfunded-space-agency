using Usa.Game.Content;
using Usa.Game.Integration.Adapters;
using Usa.Game.Integration.Runtime;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// The console's Godot-free side of one mission: handles the runtime context's events (<see cref="ConsoleRuntimeEvents"/>),
/// runs the confirmation dialog (<see cref="Confirmations"/>) for guarded controls and unsafe stops, and follows the app
/// freeze. The console scene shows what this exposes and forwards the player's answers.
/// </summary>
public sealed class ConsoleRuntimeBinding : IConsoleRuntimeSink, IDisposable
{
    private readonly ConsoleRuntimeEvents _events;
    private readonly IDisposable? _frozenSubscription;
    private bool _disposed;

    /// <param name="frozen">The app freeze (<c>FreezeController.Frozen</c>); null when the game never freezes (tests, tools).</param>
    /// <param name="text">Resolves confirmation effect keys (<c>impact.*</c>); null shows the keys.</param>
    public ConsoleRuntimeBinding(MissionRuntimeContext context, IReadOnlyObservable<bool>? frozen = null, ITextSource? text = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        Confirmations = new ConfirmationCoordinator(
            context.Console.Systems, context.Console.TaskSlot, () => context.Bridge.ConfirmStopTask(), context.Bridge.CancelStopTask, text);
        _events = new ConsoleRuntimeEvents(context, this);
        _frozenSubscription = frozen?.Subscribe(SetFrozen);
    }

    public MissionRuntimeContext Context { get; }

    public ConfirmationCoordinator Confirmations { get; }

    public CaptionHold Captions { get; } = new();

    /// <summary>Text (and suggested request) for the comms input.</summary>
    public event Action<CommsPrefill>? PrefillRequested;

    /// <summary>A submitted console or workspace command and the session's answer, for the comms panel's system lines.</summary>
    public event Action<CommandRequest, ValidationResult>? CommandAnswered;

    public bool IsFrozen { get; private set; }

    public void SetFrozen(bool frozen)
    {
        IsFrozen = frozen;
        Confirmations.SetFrozen(frozen);
        Captions.SetFrozen(frozen);
    }

    /// <summary>Per-frame real time for the dialog countdown and the caption hold.</summary>
    public void Advance(double realSeconds)
    {
        Confirmations.Advance(realSeconds);
        Captions.Advance(realSeconds);
    }

    void IConsoleRuntimeSink.ConfirmStop(TaskId task) => Confirmations.ShowStop();

    void IConsoleRuntimeSink.Prefill(CommsPrefill prefill) => PrefillRequested?.Invoke(prefill);

    void IConsoleRuntimeSink.CommandAnswered(CommandRequest request, ValidationResult result) => CommandAnswered?.Invoke(request, result);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frozenSubscription?.Dispose();
        _events.Dispose();
        Confirmations.Dispose();
        Captions.Clear();
    }
}
