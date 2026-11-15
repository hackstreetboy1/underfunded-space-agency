using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Tasks;

/// <summary>
/// The single crew task slot (UI.md 3.7, GDD 5.3): assignee, task name, progress, ETA in MET and real seconds at
/// the current compression, complexity pips and Stop. A rejected second task flashes the bar once
/// (<see cref="BusyFlashed"/>). Progress and ETA change every frame, so they are throttled when a throttle is given.
/// </summary>
public sealed class TaskSlotViewModel : ViewModelBase
{
    public const int MaxComplexity = 5;

    private readonly ConsoleWords _words;
    private readonly ObservableValue<bool> _hasTask = new();
    private readonly ObservableValue<string> _assigneeName = new(string.Empty);
    private readonly ObservableValue<string> _taskName = new(string.Empty);
    private readonly ObservableValue<double> _progress;
    private readonly ObservableValue<string> _progressText;
    private readonly ObservableValue<TaskEta?> _eta;
    private readonly ObservableValue<ComplexityPips> _complexity = new(new ComplexityPips(0, MaxComplexity));
    private readonly ObservableValue<bool> _stopEnabled = new();
    private readonly ObservableValue<bool> _stopNeedsConfirmation = new();
    private readonly Signal<StopTaskRequest> _stopRequested = new();
    private readonly Signal<BusyFlash> _busyFlashed = new();
    private ActiveTaskSnapshot? _task;

    public TaskSlotViewModel(ConsoleWords words, UpdateThrottle? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
        _progress = ConsoleObservables.Throttled(0.0, throttle);
        _progressText = ConsoleObservables.Throttled(string.Empty, throttle);
        _eta = ConsoleObservables.Throttled<TaskEta?>(null, throttle);
    }

    /// <summary>Shown in the slot while <see cref="HasTask"/> is false.</summary>
    public string EmptyText => _words.NoActiveTask;

    public IReadOnlyObservable<bool> HasTask => _hasTask;

    public TaskId? TaskId => _task?.Id;

    public CrewId? AssigneeId => _task?.AssigneeId;

    public IReadOnlyObservable<string> AssigneeName => _assigneeName;

    public IReadOnlyObservable<string> TaskName => _taskName;

    /// <summary>0-1.</summary>
    public IReadOnlyObservable<double> Progress => _progress;

    /// <summary><c>42%</c>.</summary>
    public IReadOnlyObservable<string> ProgressText => _progressText;

    /// <summary>Null without a task.</summary>
    public IReadOnlyObservable<TaskEta?> Eta => _eta;

    public IReadOnlyObservable<ComplexityPips> Complexity => _complexity;

    public IReadOnlyObservable<bool> StopEnabled => _stopEnabled;

    public IReadOnlyObservable<bool> StopNeedsConfirmation => _stopNeedsConfirmation;

    public Signal<StopTaskRequest> StopRequested => _stopRequested;

    public Signal<BusyFlash> BusyFlashed => _busyFlashed;

    /// <param name="compressionRate">MET seconds per real second, for the real-time ETA.</param>
    public void Apply(ActiveTaskSnapshot? task, MissionTime now, double compressionRate)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _task = task;
        if (task is null)
        {
            _hasTask.Value = false;
            _assigneeName.Value = string.Empty;
            _taskName.Value = string.Empty;
            _progress.Value = 0;
            _progressText.Value = string.Empty;
            _eta.Value = null;
            _complexity.Value = new ComplexityPips(0, MaxComplexity);
            _stopEnabled.Value = false;
            _stopNeedsConfirmation.Value = false;
            return;
        }

        var progress = ProgressOf(task, now);
        _hasTask.Value = true;
        _assigneeName.Value = task.AssigneeName;
        _taskName.Value = task.Name;
        _progress.Value = progress;
        _progressText.Value = ReadoutFormatter.FormatPercent(Math.Floor(progress * 100));
        _eta.Value = TaskEta.For(task, now, compressionRate, _words);
        _complexity.Value = new ComplexityPips(Math.Clamp(task.Complexity, 1, MaxComplexity), MaxComplexity);
        _stopEnabled.Value = task.Interruptible;
        _stopNeedsConfirmation.Value = task.Interruptible && task.UnsafeToInterrupt;
    }

    /// <summary>Stop button: raises <see cref="StopRequested"/> when the task can be interrupted.</summary>
    public bool RequestStop()
    {
        if (_task is not { Interruptible: true } task) return false;
        _stopRequested.Raise(new StopTaskRequest(task.Id, task.UnsafeToInterrupt));
        return true;
    }

    /// <summary>Called when the simulation rejects a second task because the slot is busy.</summary>
    public BusyFlash ReportCrewBusy()
    {
        var flash = _task is { } task
            ? new BusyFlash(_words.CrewBusyOn(task.AssigneeName, task.Name), task.AssigneeId, task.Id)
            : new BusyFlash(_words.CrewBusy, null, null);
        _busyFlashed.Raise(flash);
        return flash;
    }

    private static double ProgressOf(ActiveTaskSnapshot task, MissionTime now)
    {
        if (task.Progress is { } reported) return double.IsFinite(reported) ? Math.Clamp(reported, 0, 1) : 0;
        var durationMs = task.EstimatedEnd.Milliseconds - task.StartedAt.Milliseconds;
        if (durationMs <= 0) return 1;
        return Math.Clamp((now.Milliseconds - task.StartedAt.Milliseconds) / (double)durationMs, 0, 1);
    }
}
