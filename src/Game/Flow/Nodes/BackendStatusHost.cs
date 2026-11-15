using Godot;
using Usa.Game.Flow.Backend;
using Usa.Game.Services.Backend;
using Usa.Game.Services.Connection;

namespace Usa.Game.Flow.Nodes;

/// <summary>
/// Feeds every heartbeat into the <see cref="BackendStatusTracker"/>, ticks it once a second (so a proxy the launcher started
/// is polled every second until it answers) and stops that proxy when the game quits. Runs while the tree is paused.
/// Call <see cref="Initialize"/> before adding it to the tree.
/// </summary>
public sealed partial class BackendStatusHost : Node
{
    public const double TickSeconds = 1.0;

    private BackendStatusTracker _tracker = null!;
    private ConnectionMonitor _connection = null!;
    private BackendLauncher? _launcher;
    private Godot.Timer _timer = null!;

    public void Initialize(BackendStatusTracker tracker, ConnectionMonitor connection, BackendLauncher? launcher)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _launcher = launcher;
        // Subscribed before the monitor's first heartbeat completes, so the boot report is never missed.
        _connection.ReportUpdated += _tracker.Observe;
    }

    public override void _Ready()
    {
        if (_tracker is null) throw new InvalidOperationException($"{nameof(BackendStatusHost)}.{nameof(Initialize)} must be called before adding it to the tree.");
        Name = nameof(BackendStatusHost);
        ProcessMode = ProcessModeEnum.Always;
        _timer = new Godot.Timer { WaitTime = TickSeconds, OneShot = false, Autostart = true };
        _timer.Timeout += _tracker.Tick;
        AddChild(_timer);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) StopProxy();
    }

    public override void _ExitTree()
    {
        _connection.ReportUpdated -= _tracker.Observe;
        StopProxy();
    }

    /// <summary>Stops a proxy the game started (quit); a proxy the player started is never touched.</summary>
    public void StopProxy() => _launcher?.Stop();
}
