using Godot;
using Usa.Game.Services.Ai;
using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Connection;

/// <summary>
/// Heartbeat for the essential services (GDD 15.6, SCRIPTING.md 13.4): <c>GET /health</c> every 5 s and
/// immediately after any request that suggests an outage. Raises <see cref="Lost"/> / <see cref="Restored"/>;
/// it holds no simulation reference, so the host decides what to freeze. Runs while the tree is paused.
/// The heartbeat logic lives in <see cref="ConnectionProbe"/>; quitting (window close, <see cref="Shutdown"/>, leaving the
/// tree) cancels the heartbeat in flight without counting it as a failure.
/// Call <see cref="Initialize"/> before adding the node to the tree.
/// </summary>
public sealed partial class ConnectionMonitor : Node
{
	public const double HeartbeatSeconds = 5.0;

	private const string Category = "Connection";

	private readonly List<Action<ConnectionReport>> _reportUpdated = [];
	private readonly List<Action<ConnectionReport>> _lost = [];
	private readonly List<Action<ConnectionReport>> _restored = [];
	private IAiHealthSource _source = null!;
	private Log _log = null!;
	private ConnectionProbe _probe = null!;
	private Godot.Timer _timer = null!;

	/// <summary>Every heartbeat result, for the overlay's per-service status lines.</summary>
	/// <remarks>Each subscriber of this and the other events is called in isolation: one that throws is logged, the rest still run.</remarks>
	public event Action<ConnectionReport>? ReportUpdated
	{
		add => Add(_reportUpdated, value);
		remove => Remove(_reportUpdated, value);
	}

	/// <summary>Two consecutive failed heartbeats. The report names the failing service(s).</summary>
	public event Action<ConnectionReport>? Lost
	{
		add => Add(_lost, value);
		remove => Remove(_lost, value);
	}

	/// <summary>Two consecutive healthy heartbeats after <see cref="Lost"/>.</summary>
	public event Action<ConnectionReport>? Restored
	{
		add => Add(_restored, value);
		remove => Remove(_restored, value);
	}

	public ConnectionReport LastReport => _probe?.LastReport ?? ConnectionReport.Initial;

	public bool IsLost => _probe?.IsLost ?? false;

	/// <summary>Seconds until the next scheduled heartbeat, for the overlay's retry countdown.</summary>
	public double SecondsUntilNextProbe => _timer?.TimeLeft ?? 0;

	public void Initialize(IAiHealthSource source, Log log, TimeProvider? time = null)
	{
		_source = source ?? throw new ArgumentNullException(nameof(source));
		_log = log ?? throw new ArgumentNullException(nameof(log));
		_probe = new ConnectionProbe(source, log, time);
		_probe.ReportUpdated += report => Raise(_reportUpdated, report, nameof(ReportUpdated));
		_probe.Lost += report => Raise(_lost, report, nameof(Lost));
		_probe.Restored += report => Raise(_restored, report, nameof(Restored));
	}

	public override void _Ready()
	{
		if (_probe is null) throw new InvalidOperationException($"{nameof(ConnectionMonitor)}.{nameof(Initialize)} must be called before adding it to the tree.");

		Name = nameof(ConnectionMonitor);
		ProcessMode = ProcessModeEnum.Always;
		_timer = new Godot.Timer { WaitTime = HeartbeatSeconds, OneShot = false, Autostart = true };
		_timer.Timeout += ProbeNow;
		AddChild(_timer);
		_source.OutageSuspected += OnOutageSuspected;
		ProbeNow();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest) Shutdown();
	}

	public override void _ExitTree()
	{
		_source.OutageSuspected -= OnOutageSuspected;
		Shutdown();
		_probe.Dispose();
	}

	/// <summary>Runs a heartbeat now ("Retry now"); coalesces with one already in flight.</summary>
	public void ProbeNow()
	{
		if (_probe.IsShutDown) return;
		if (!_probe.IsProbing) _timer.Start();
		// RequestAsync never faults; failures are logged inside the probe.
		_ = _probe.RequestAsync();
	}

	/// <summary>Stops heartbeats for good and drops the one in flight (quit). Call before disposing the health source.</summary>
	public void Shutdown()
	{
		_probe.BeginShutdown();
		if (IsInstanceValid(_timer)) _timer.Stop();
	}

	private static void Add(List<Action<ConnectionReport>> handlers, Action<ConnectionReport>? handler)
	{
		if (handler is not null) handlers.Add(handler);
	}

	private static void Remove(List<Action<ConnectionReport>> handlers, Action<ConnectionReport>? handler)
	{
		if (handler is not null) handlers.Remove(handler);
	}

	// One subscriber that throws (a UI handler) must not keep the others (the router, comms) from hearing about the outage.
	private void Raise(List<Action<ConnectionReport>> handlers, ConnectionReport report, string name)
	{
		foreach (var handler in handlers.ToArray())
		{
			try
			{
				handler(report);
			}
			catch (Exception ex)
			{
				_log.Error(Category, $"{name} handler failed: {ex.GetType().Name}: {ex.Message}");
			}
		}
	}

	// Deferred so a probe never starts inside the gateway call that suspected the outage.
	private void OnOutageSuspected(AiError error) => Callable.From(ProbeNow).CallDeferred();
}
