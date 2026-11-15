using Godot;
using Usa.Game.Bootstrap;
using Usa.Game.Diagnostics.Crash;
using Usa.Game.Diagnostics.Repro;
using Usa.Game.Input;
using Usa.Simulation.Session;

namespace Usa.Game.Diagnostics.Overlay;

/// <summary>
/// The F12 developer overlay (debug builds only, see <see cref="DebugOverlayGate"/>): mission, difficulty, seed, run tag, MET,
/// compression and its reason, freeze reasons, state hash, frame time, backend status and the last commands, with
/// "Copy repro" and "Save repro". It shows no truth state: only ids, time, pacing, opaque hashes and the player's own
/// commands, so opening it never tells the player more than the console does. Call <see cref="Initialize"/> before adding it.
/// </summary>
public sealed partial class DebugOverlay : CanvasLayer
{
    public const string ScenePath = "res://ui/debug/DebugOverlay.tscn";
    private const double RefreshIntervalS = 0.25;
    private const string Category = "DebugOverlay";

    private GameServices _services = null!;
    private GameReproCapture _capture = null!;
    private ReproStore _store = null!;
    private CrashReporter? _crash;
    private string _version = "";
    private Label _status = null!;
    private Label _commands = null!;
    private double _sinceRefreshS;
    private double _frameMs;
    private string? _notice;
    private string? _crashNotice;
    private MissionSession? _hashedSession;
    private StateHashSample? _liveHash;

    public void Initialize(GameServices services, GameReproCapture capture, ReproStore store, CrashReporter? crash, string version)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _crash = crash;
        _version = version;
    }

    public override void _Ready()
    {
        if (_services is null) throw new InvalidOperationException($"{nameof(DebugOverlay)}.{nameof(Initialize)} must be called before adding it to the tree.");
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        _status = GetNode<Label>("%Status");
        _commands = GetNode<Label>("%Commands");
        GetNode<Button>("%CopyRepro").Pressed += CopyRepro;
        GetNode<Button>("%SaveRepro").Pressed += SaveRepro;
        GetNode<Button>("%HashNow").Pressed += HashNow;
    }

    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(InputBindings.DebugOverlay)) return;
        Visible = !Visible;
        if (Visible) Refresh();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _frameMs = delta * 1000.0;
        if (!Visible) return;
        _sinceRefreshS += delta;
        if (_sinceRefreshS < RefreshIntervalS) return;
        _sinceRefreshS = 0;
        Refresh();
    }

    private void Refresh()
    {
        if (_crash?.LastNotice is { } crashNotice && crashNotice != _crashNotice)
        {
            _crashNotice = crashNotice;
            _notice = crashNotice;
        }
        var frame = new DebugOverlayFrame(
            _version,
            _services.Router.Current.ToString(),
            _services.Freeze.Reasons.Value.ToString(),
            BackendStatusSource.Describe(_services),
            Engine.GetFramesPerSecond(),
            _frameMs,
            Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
            Mission(),
            _notice);
        _status.Text = DebugOverlayText.Status(frame);
        _commands.Text = DebugOverlayText.Commands(frame.Mission);
    }

    private DebugOverlayMission? Mission()
    {
        if (_services.Flow.Runtime is not { } runtime) return null;
        var session = runtime.Mission.Session;
        if (!ReferenceEquals(session, _hashedSession)) _liveHash = null;
        var host = _services.Flow.Host;
        var samples = session.HashSamples;
        return new DebugOverlayMission(
            session.MissionId,
            session.DifficultyId,
            session.Seed,
            ReproBuilder.RunTag(session),
            session.Now,
            host.CompressionRate,
            host.CompressionReason.ToString(),
            host.EffectiveRate,
            host.IsAdvancingToEvent,
            session.FreezeReasons.ToString(),
            samples.Count > 0 ? samples[^1] : null,
            samples.Count,
            _liveHash,
            session.CommandLog.Entries);
    }

    private void HashNow()
    {
        if (_services.Flow.Runtime is not { } runtime) return;
        var session = runtime.Mission.Session;
        _hashedSession = session;
        _liveHash = new StateHashSample(session.Now, session.StateHash());
        Refresh();
    }

    private void CopyRepro()
    {
        var json = ReproJson.Serialize(_capture.Capture(ReproReasons.Manual));
        DisplayServer.ClipboardSet(json);
        _notice = $"repro copied to the clipboard ({json.Length / 1024.0:0.#} KB)";
        Refresh();
    }

    private void SaveRepro()
    {
        try
        {
            var path = _store.Save(_capture.Capture(ReproReasons.Manual));
            _notice = $"repro saved: {path}";
            _services.Log.Info(Category, $"repro saved to {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notice = $"repro not saved: {ex.Message}";
            _services.Log.Warn(Category, $"repro not saved: {ex.GetType().Name}: {ex.Message}");
        }
        Refresh();
    }
}
