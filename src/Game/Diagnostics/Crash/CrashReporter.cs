using System.Diagnostics.CodeAnalysis;
using Godot;
using Usa.Game.Diagnostics.Repro;
using Usa.Game.Services.Logging;

namespace Usa.Game.Diagnostics.Crash;

/// <summary>
/// Unhandled-exception handler (every build): exceptions from Godot's error stream (<see cref="ErrorStreamLogger"/>),
/// unobserved task exceptions and a dying process (<see cref="AppDomain.UnhandledException"/>) are logged and each distinct
/// one (type + first stack frame) writes a repro to <c>user://repros</c>, at most <see cref="MaxReprosPerLaunch"/> per launch.
/// Each saved (or failed) repro is announced on the main thread through <see cref="Reported"/>, which drives the player-facing
/// <see cref="CrashNotice"/>. Call <see cref="Initialize"/> before adding it to the tree.
/// </summary>
public sealed partial class CrashReporter : Node
{
    public const int MaxReprosPerLaunch = 5;
    private const string Category = "Crash";

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private GameReproCapture _capture = null!;
    private ReproStore _store = null!;
    private Log _log = null!;
    private ErrorStreamLogger? _logger;
    private int _saved;
    private bool _capWarned;
    private CrashReport? _pending;

    /// <summary>The newest crash notice (a saved repro path), for the debug overlay.</summary>
    public string? LastNotice { get; private set; }

    /// <summary>Raised on the main thread once a crash was reported (never for a dying process, which has no next frame).</summary>
    public event Action<CrashReport>? Reported;

    public void Initialize(GameReproCapture capture, ReproStore store, Log log)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public override void _EnterTree()
    {
        if (_capture is null) throw new InvalidOperationException($"{nameof(CrashReporter)}.{nameof(Initialize)} must be called before adding it to the tree.");
        ProcessMode = ProcessModeEnum.Always;
        _logger = new ErrorStreamLogger();
        OS.AddLogger(_logger);
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public override void _ExitTree()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        if (_logger is null) return;
        OS.RemoveLogger(_logger);
        _logger = null;
    }

    public override void _Process(double delta)
    {
        if (_logger is null) return;
        while (_logger.TryDequeue(out var failure)) Report(failure);
        CrashReport? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }
        if (pending is not null) Announce(pending);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A failing notice must not keep reporting itself as a new crash.")]
    private void Announce(CrashReport report)
    {
        try
        {
            Reported?.Invoke(report);
        }
        catch (Exception ex)
        {
            _log.Error(Category, $"crash notice failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The process is going down on this thread: write the repro now, best effort.
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception) Report(UnhandledErrorFilter.FromException(exception, "appdomain"));
    }

    // Raised on the finalizer thread: queue it for the main thread.
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        foreach (var inner in args.Exception.Flatten().InnerExceptions)
        {
            if (!UnhandledErrorFilter.IsExpected(inner)) _logger?.Enqueue(UnhandledErrorFilter.FromException(inner, "task"));
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A crash reporter that throws would hide the crash it reports.")]
    private void Report(ReproFailure failure)
    {
        lock (_gate)
        {
            if (!_seen.Add(UnhandledErrorFilter.Signature(failure))) return;
            using var _ = _logger?.SuppressCurrentThread();
            if (_saved >= MaxReprosPerLaunch)
            {
                if (!_capWarned) _log.Warn(Category, $"{MaxReprosPerLaunch} crash repros written this launch; later failures are logged only");
                _capWarned = true;
                _log.Error(Category, $"unhandled {failure.Type} ({failure.Source}) {failure.Message}");
                return;
            }

            _saved++;
            try
            {
                var path = _store.Save(_capture.Capture(ReproReasons.Exception, failure));
                LastNotice = $"crash repro saved: {path}";
                _pending = new CrashReport(path);
                _log.Error(Category, $"unhandled {failure.Type} ({failure.Source}) {failure.Message}; repro saved to {path}");
            }
            catch (Exception ex)
            {
                LastNotice = $"crash repro failed: {ex.GetType().Name}";
                _pending = new CrashReport(null);
                _log.Error(Category, $"unhandled {failure.Type} ({failure.Source}) {failure.Message}; repro not saved ({ex.GetType().Name}: {ex.Message})");
            }
        }
    }
}

/// <param name="ReproPath">Where the repro was written; null when it could not be saved.</param>
public sealed record CrashReport(string? ReproPath);
