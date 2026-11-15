using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Header;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.Presentation.Console.Tasks;
using Usa.Game.Presentation.Console.Tips;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Text placed in the comms input for the player to review and send (nothing executes until they do).
/// <paramref name="Suggested"/> is the request the text stands for when it maps to one action (an "Ask" on a readable fact
/// becomes <c>read_fact</c>), so the comms panel can send it without interpretation; it takes its id when sent.
/// </summary>
public sealed record CommsPrefill(string Text, CommandDraft? Suggested = null, ComponentId? Component = null, string? FactKey = null);

/// <summary>
/// The console's outgoing signals turned into simulation requests (SCRIPTING 11.3: one input path): remote switches →
/// remote actions (<c>RemoteControl</c> source, confirmed when the guard dialog was accepted), acknowledge → <c>acknowledge_alarms</c>,
/// Stop → <c>stop_task</c>, "Request…" and "Ask" → comms prefill, tips → the guidance director, advance → the host pacer.
/// Command ids come from the injected provider and are drawn only when a request is sent.
/// </summary>
public sealed class ConsoleCommandBridge
{
    private const string ActiveTaskPath = "crew.task.id";

    /// <summary>The refusal of a request the tutorial rails do not allow (its detail is the guide's redirect line).</summary>
    public const string OffScriptReason = "tutorial_off_script";

    private readonly MissionSession _session;
    private readonly RemoteControlMap _controls;
    private readonly Func<CommandRequest, ValidationResult> _submit;
    private readonly Func<CommandId> _nextCommandId;
    private readonly Func<bool> _advanceToNextEvent;
    private readonly Dictionary<string, (CatalogAction Action, string Parameter)> _readFacts = new(StringComparer.Ordinal);
    private readonly ActionId? _stop;
    private readonly ActionId? _acknowledge;
    private readonly ITutorialRails? _rails;
    private TaskId? _pendingStop;

    /// <param name="advanceToNextEvent">The host's "advance to next event" (<c>SimulationHost.RequestAdvanceToNextEvent</c>).</param>
    /// <param name="submit">The command pipeline; defaults to <see cref="MissionSession.Submit"/>.</param>
    /// <param name="nextCommandId">The run's single id provider; defaults to <see cref="MissionSession.NextCommandId"/>.</param>
    /// <param name="rails">The guided first run's tutorial rails: a request they do not allow is refused here, never submitted.</param>
    public ConsoleCommandBridge(
        MissionSession session,
        RemoteControlMap controls,
        Func<bool> advanceToNextEvent,
        Func<CommandRequest, ValidationResult>? submit = null,
        Func<CommandId>? nextCommandId = null,
        ITutorialRails? rails = null)
    {
        _rails = rails;
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(advanceToNextEvent);
        _session = session;
        _controls = controls;
        _advanceToNextEvent = advanceToNextEvent;
        _submit = submit ?? session.Submit;
        _nextCommandId = nextCommandId ?? session.NextCommandId;

        foreach (var action in session.Actions.Actions)
        {
            switch (action.Definition)
            {
                case { Meta: MetaCommandKind.StopTask }:
                    _stop ??= action.Id;
                    break;
                case { Meta: MetaCommandKind.AcknowledgeAlarms }:
                    _acknowledge ??= action.Id;
                    break;
                case { Kind: ActionKind.GlanceRead, Glance.FactParam: { } parameter }:
                    if (action.FindParameter(parameter) is { Values: { } values })
                    {
                        foreach (var value in values) _readFacts.TryAdd(value, (action, parameter));
                    }
                    break;
            }
        }
    }

    /// <summary>Every request this bridge sent, with the pipeline's answer (refusals and confirmations are shown by the caller).</summary>
    public event Action<CommandRequest, ValidationResult>? Submitted;

    public event Action<CommsPrefill>? PrefillRequested;

    /// <summary>Stop on a task that is unsafe to interrupt: show the confirmation dialog, then call <see cref="ConfirmStopTask"/>.</summary>
    public event Action<TaskId>? StopConfirmationRequested;

    /// <summary>The task a stop confirmation is open for; null when none is.</summary>
    public TaskId? PendingStop => _pendingStop;

    /// <summary>Subscribes to every outgoing signal of <paramref name="console"/>; dispose to detach.</summary>
    public IDisposable Attach(MissionConsoleViewModel console)
    {
        ArgumentNullException.ThrowIfNull(console);
        var subscriptions = new List<IDisposable>
        {
            console.Systems.Controls.ExecuteRequested.Subscribe(intent => Execute(intent)),
            console.Systems.Controls.CrewOrderRequested.Subscribe(RequestCrewOrder),
            console.Alarms.AcknowledgeRequested.Subscribe(request => Acknowledge(request)),
            console.TaskSlot.StopRequested.Subscribe(request => Stop(request)),
            console.Header.AdvanceRequested.Subscribe(request => AdvanceToNextEvent(request)),
            console.AskRequested.Subscribe(Ask),
            console.Tip.StrongerHintRequested.Subscribe(request => StrongerHint(request)),
            console.Tip.Dismissed.Subscribe(_ => DismissTip()),
        };
        return new Subscriptions(subscriptions);
    }

    /// <returns>The pipeline's answer; null when no remote action drives the component.</returns>
    public ValidationResult? Execute(RemoteControlIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!_controls.IsRemote(intent.ComponentId) || intent.DesiredState is not (DesiredControlState.On or DesiredControlState.Off)) return null;
        return _controls.Request(intent, _nextCommandId()) is { } request ? Send(request) : null;
    }

    public void RequestCrewOrder(CrewOrderDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        PrefillRequested?.Invoke(new CommsPrefill(draft.Text, Component: draft.ComponentId));
    }

    public ValidationResult? Acknowledge(AcknowledgeAlarmsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _acknowledge is { } action ? Send(Meta(action, confirmed: false)) : null;
    }

    public ValidationResult? Stop(StopTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.NeedsConfirmation)
        {
            _pendingStop = request.TaskId;
            StopConfirmationRequested?.Invoke(request.TaskId);
            return null;
        }
        return _stop is { } action ? Send(Meta(action, confirmed: true)) : null;
    }

    /// <summary>
    /// The player accepted the stop confirmation. <c>stop_task</c> stops whatever task runs, so it is sent only while the task
    /// the dialog was opened for is still the one running; otherwise nothing is sent and null is returned.
    /// </summary>
    public ValidationResult? ConfirmStopTask()
    {
        if (_pendingStop is not { } task || _stop is not { } action) return null;
        _pendingStop = null;
        return IsRunning(task) ? Send(Meta(action, confirmed: true)) : null;
    }

    public void CancelStopTask() => _pendingStop = null;

    public bool AdvanceToNextEvent(AdvanceToNextEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _advanceToNextEvent();
    }

    /// <summary>"Ask" on an unknown value: the draft text, plus a <c>read_fact</c> draft when the key is a readable fact.</summary>
    public void Ask(AskDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        PrefillRequested?.Invoke(new CommsPrefill(draft.Text, ReadFactDraft(draft.Key), FactKey: draft.Key));
    }

    /// <summary>The glance read of <paramref name="factKey"/> by any crew member; null when no glance action reads it.</summary>
    public CommandDraft? ReadFactDraft(string factKey)
    {
        ArgumentNullException.ThrowIfNull(factKey);
        if (!_readFacts.TryGetValue(factKey, out var read)) return null;
        var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { [read.Parameter] = CommandArg.Of(factKey) };
        return new CommandDraft(read.Action.Id, CommandSource.QuickCommand, CommandRequest.AnyCrew, args);
    }

    /// <summary>Sends a draft (a prefill's suggestion the player accepted) with a fresh id.</summary>
    public ValidationResult Send(CommandDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return Send(draft.ToRequest(_nextCommandId()));
    }

    public bool StrongerHint(StrongerHintRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _session.Guidance.RequestStrongerHint(_session.Now);
    }

    public bool DismissTip() => _session.Guidance.Dismiss(_session.Now);

    private bool IsRunning(TaskId task) =>
        _session.State.TryGet(ActiveTaskPath, out var active) && active.Kind == StateValueKind.Text &&
        string.Equals(active.AsText(), task.Value, StringComparison.Ordinal);

    private CommandRequest Meta(ActionId action, bool confirmed) =>
        new(_nextCommandId(), CommandSource.QuickCommand, action, null, CommandRequest.NoArgs, confirmed);

    private ValidationResult Send(CommandRequest request)
    {
        var result = _rails is { } rails && !rails.Allows(request)
            ? ValidationResult.Refused(OffScriptReason, rails.RedirectLine)
            : _submit(request);
        Submitted?.Invoke(request, result);
        return result;
    }

    private sealed class Subscriptions(List<IDisposable> items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items) item.Dispose();
            items.Clear();
        }
    }
}
