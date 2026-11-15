using Usa.Simulation.Commands;
using Usa.Simulation.Crew;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario;

namespace Usa.Simulation.Session;

/// <summary>
/// <see cref="ITutorialRails"/> fed with the session's drained events (<see cref="Observe"/>): a railed beat shown makes it the
/// pending step; its close (done or moot) moves to "wait" until the director shows the next one, so the guide follows the
/// simulation whenever it skips ahead; a crew task of the step's order that fails or is stopped turns into "try again" with the
/// same order. A presentation-side observer: it never changes the session.
/// </summary>
public sealed class TutorialRails : ITutorialRails
{
    private static readonly string[] PlannerAnchors = ["planner_engine", "planner_goal", "planner_propose", "planner_execute", "confirm_dialog"];
    private const string RetrofireTask = "retrofire";

    private readonly Dictionary<string, (int Number, TutorialBeat Beat)> _steps = new(StringComparer.Ordinal);
    private readonly Func<ActionId, bool> _harmless;
    private readonly int _total;
    private readonly int _proposeStep;
    private readonly int _executeStep;
    private readonly int _lastPlannerStep;
    private readonly string? _engine;
    private readonly string? _zone;

    /// <param name="harmless">Requests always allowed: reads and acknowledging alarms (never a crew task or a switch).</param>
    public TutorialRails(TutorialScript script, Func<ActionId, bool> harmless)
    {
        ArgumentNullException.ThrowIfNull(script);
        _harmless = harmless ?? throw new ArgumentNullException(nameof(harmless));
        foreach (var beat in script.Beats)
        {
            if (beat.Rail is not { } rail) continue;
            var number = _steps.Count + 1;
            _steps[beat.Id] = (number, beat);
            switch (beat.Anchor)
            {
                case "planner_engine":
                    _engine ??= rail.Choose;
                    break;
                case "planner_goal":
                    _zone ??= rail.Choose;
                    break;
                case "planner_propose":
                    _proposeStep = number;
                    break;
                case "planner_execute":
                    _executeStep = number;
                    break;
            }
            if (Array.IndexOf(PlannerAnchors, beat.Anchor) >= 0) _lastPlannerStep = number;
        }
        _total = _steps.Count;
    }

    public event Action? Changed;

    public bool IsEngaged => Step is not null;

    public TutorialRailsStep? Step { get; private set; }

    /// <summary>The engine the planner steps lock the dropdown to (<see cref="TutorialRailData.Choose"/>).</summary>
    public string? LockedEngine => _engine;

    /// <summary>The recovery zone the planner steps lock the goal picker to.</summary>
    public string? LockedZone => _zone;

    /// <summary>
    /// The main view the current step last asked the console to show (<see cref="TutorialRailData.View"/>); each request bumps
    /// <see cref="ViewRequestSerial"/>, so the console switches once and the player may switch away again.
    /// </summary>
    public string? RequestedView { get; private set; }

    public int ViewRequestSerial { get; private set; }

    public string BlockedTooltip => Step is { } step ? $"Follow the guide — step {step.Number} of {step.Total}" : string.Empty;

    public string RedirectLine
    {
        get
        {
            if (Step is not { } step) return string.Empty;
            if (step.Phase == TutorialRailsPhase.Wait || (step.Order is null && step.Instruction.StartsWith("Wait", StringComparison.Ordinal)))
                return $"Let's stick to the checklist. {step.Instruction}";
            var next = step.Order?.Text ?? step.Rail.Do;
            return $"Let's stick to the checklist — next is: {next.TrimEnd('.')}.";
        }
    }

    /// <summary>A resumed run: picks up the step on screen, or the wait after the last step done.</summary>
    public void Resume(TutorialDirector director, IStateQuery state)
    {
        ArgumentNullException.ThrowIfNull(director);
        ArgumentNullException.ThrowIfNull(state);
        if (!director.IsEnabled || director.IsFinished)
        {
            Set(null);
            return;
        }
        if (director.ActiveBeatId is { } active)
        {
            Set(_steps.TryGetValue(active, out var shown) ? StepOf(shown, shown.Beat.ResolveOrder(state), TutorialRailsPhase.Do) : null);
            return;
        }
        (int Number, TutorialBeat Beat)? last = null;
        foreach (var step in _steps.Values)
        {
            if (director.StatusOf(step.Beat.Id) == TutorialBeatStatus.Done) last = step;
        }
        Set(last is { } done && done.Number < _total ? StepOf(done, done.Beat.ResolveOrder(state), TutorialRailsPhase.Wait) : null);
    }

    /// <summary>Hand every drained session event here, in order.</summary>
    public void Observe(ISimEvent simEvent)
    {
        switch (simEvent)
        {
            case TutorialCalloutShown shown:
                Set(_steps.TryGetValue(shown.BeatId, out var step) ? StepOf(step, shown.Order, TutorialRailsPhase.Do) : null);
                if (Step?.Rail is { View: { } shownView, ViewOn: null }) RequestView(shownView);
                break;
            case TutorialCalloutClosed closed when Step is { } current && current.BeatId == closed.BeatId:
                var ends = closed.Reason is TutorialCloseReason.Disabled or TutorialCloseReason.Ended || current.Number >= _total;
                Set(ends ? null : current with { Phase = TutorialRailsPhase.Wait });
                if (!ends && current.Rail is { View: { } waitView, ViewOn: TutorialRailData.ViewOnWait }) RequestView(waitView);
                break;
            case MissionEventRaised raised when Step?.Rail is { View: { } eventView, ViewOn: { } on } && on == raised.EventId:
                RequestView(eventView);
                break;
            case TutorialFinished:
                Set(null);
                break;
            case CrewTaskFailed failed:
                OnTaskEnded(failed.Task);
                break;
            case CrewTaskStopped stopped:
                OnTaskEnded(stopped.Task);
                break;
            case CrewTaskStarted started when Step is { Phase: TutorialRailsPhase.Retry } retry && IsStepTask(retry, started.Task):
                Set(retry with { Phase = TutorialRailsPhase.Wait });
                break;
        }
    }

    public bool IsAllowed(string control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (Step is not { } step) return true;
        var colon = control.IndexOf(':', StringComparison.Ordinal);
        var id = colon < 0 ? control : control[..colon];
        var item = colon < 0 ? null : control[(colon + 1)..];
        var acting = step.Phase != TutorialRailsPhase.Wait;
        return id switch
        {
            // Looking, typing (comms holds orders to the script itself), opening the planner, silencing an alarm, the menu
            // and the end-of-mission button never take the player off the path.
            TutorialControls.ViewTab or TutorialControls.Menu or TutorialControls.Advance or TutorialControls.CommsInput
                or TutorialControls.TranscriptOffer or TutorialControls.AlarmAck or TutorialControls.PlannerToggle
                or TutorialControls.ProceedDebrief => true,
            TutorialControls.CommsQuick => acting && step.Order is { } order && item == order.Action.Value,
            TutorialControls.PlannerEngine => InPlanner(step) && (item is null || _engine is null || item == _engine),
            TutorialControls.PlannerGoal => InPlanner(step) && (item is null || _zone is null || item == _zone),
            TutorialControls.PlannerPropose => InPlanner(step) && (IsPlannerRetry(step) || step.Number >= _proposeStep),
            TutorialControls.PlannerExecute => InPlanner(step) && (IsPlannerRetry(step) || step.Number >= _executeStep),
            _ => false,
        };
    }

    public bool Allows(CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Step is not { } step || _harmless(request.Action)) return true;
        return step.Phase != TutorialRailsPhase.Wait && step.Order is { } order && Matches(order, request);
    }

    /// <summary>The step's order with any crew member and at least the step's arguments.</summary>
    public static bool Matches(TutorialOrder order, CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != order.Action) return false;
        foreach (var (name, value) in order.Args)
        {
            if (!request.Args.TryGetValue(name, out var given) ||
                !string.Equals(given.ToCanonicalString(), value.ToCanonicalString(), StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    // A failed or stopped task of the step's order (or the retrofire the planner steps booked) is ordered again.
    private void OnTaskEnded(TaskId task)
    {
        if (Step is { Phase: TutorialRailsPhase.Wait } step && IsStepTask(step, task)) Set(step with { Phase = TutorialRailsPhase.Retry });
    }

    private bool IsStepTask(TutorialRailsStep step, TaskId task) =>
        step.Order is { } order ? order.Action.Value == task.Value : task.Value == RetrofireTask && IsPlannerWait(step);

    // The planner steps, and "try again" after the burn they booked failed: the dropdowns stay locked to the step's choices.
    private bool InPlanner(TutorialRailsStep step) =>
        step.Phase != TutorialRailsPhase.Wait && (Array.IndexOf(PlannerAnchors, step.Anchor) >= 0 || IsPlannerRetry(step));

    private bool IsPlannerRetry(TutorialRailsStep step) => step.Phase == TutorialRailsPhase.Retry && IsPlannerWait(step);

    private bool IsPlannerWait(TutorialRailsStep step) => step.Order is null && step.Number > _lastPlannerStep && _lastPlannerStep > 0 && step.Number == _lastPlannerStep + 1;

    private TutorialRailsStep StepOf((int Number, TutorialBeat Beat) step, TutorialOrder? order, TutorialRailsPhase phase) =>
        new(step.Number, _total, step.Beat.Id, step.Beat.Anchor, step.Beat.Title, step.Beat.Rail!, order, phase);

    private void RequestView(string view)
    {
        RequestedView = view;
        ViewRequestSerial++;
    }

    private void Set(TutorialRailsStep? step)
    {
        if (step == Step) return;
        Step = step;
        Changed?.Invoke();
    }
}
