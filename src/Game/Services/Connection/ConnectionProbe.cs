using Usa.Game.Services.Ai;
using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Connection;

/// <summary>
/// Godot-free core of <c>ConnectionMonitor</c>: runs one heartbeat at a time (a request while one is in flight
/// queues exactly one more), debounces the results through <see cref="ConnectionStateMachine"/> and raises the events.
/// A heartbeat ended by cancellation or by <see cref="BeginShutdown"/> is no result at all: no report, no strike, no
/// warning. Never throws; a faulting health source or subscriber is logged and the next heartbeat runs normally.
/// Main thread only (completions resume on the caller's synchronization context).
/// </summary>
public sealed class ConnectionProbe : IDisposable
{
	private const string Category = "Connection";

	private readonly IAiHealthSource _source;
	private readonly Log _log;
	private readonly TimeProvider _time;
	private readonly ConnectionStateMachine _state = new();

	// A heartbeat captures the token before awaiting, so a completion after Dispose still reads it safely.
	private readonly CancellationTokenSource _lifetime = new();
	private Task _running = Task.CompletedTask;
	private bool _queued;
	private bool _disposed;

	public ConnectionProbe(IAiHealthSource source, Log log, TimeProvider? time = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(log);
		_source = source;
		_log = log;
		_time = time ?? TimeProvider.System;
	}

	/// <summary>Every heartbeat result.</summary>
	public event Action<ConnectionReport>? ReportUpdated;

	/// <summary>Two consecutive failed heartbeats.</summary>
	public event Action<ConnectionReport>? Lost;

	/// <summary>Two consecutive healthy heartbeats after <see cref="Lost"/>.</summary>
	public event Action<ConnectionReport>? Restored;

	public ConnectionReport LastReport { get; private set; } = ConnectionReport.Initial;

	public bool IsLost => _state.IsLost;

	public bool IsProbing => !_running.IsCompleted;

	public bool IsShutDown => _lifetime.IsCancellationRequested;

	/// <summary>Starts a heartbeat, or queues one behind the heartbeat in flight. The task never faults.</summary>
	public Task RequestAsync()
	{
		if (IsShutDown) return Task.CompletedTask;
		if (IsProbing)
		{
			_queued = true;
			return _running;
		}

		_running = RunAsync();
		return _running;
	}

	/// <summary>Cancels the heartbeat in flight and ignores every later result (quit, scene teardown). Irreversible.</summary>
	public void BeginShutdown()
	{
		_queued = false;
		if (!_disposed) _lifetime.Cancel();
	}

	public void Dispose()
	{
		if (_disposed) return;
		BeginShutdown();
		_disposed = true;
		_lifetime.Dispose();
	}

	private async Task RunAsync()
	{
		do
		{
			_queued = false;
			try
			{
				await ProbeOnceAsync();
			}
			catch (Exception ex)
			{
				_log.Error(Category, $"heartbeat failed unexpectedly: {ex.GetType().Name}: {ex.Message}");
			}
		}
		while (_queued && !IsShutDown);
	}

	private async Task ProbeOnceAsync()
	{
		var token = _lifetime.Token;
		var result = await _source.GetHealthAsync(token);
		// Shutting down or cancelled: the heartbeat says nothing about the backend.
		if (token.IsCancellationRequested || result.Error?.Kind == AiFailureKind.Cancelled) return;
		Record(ConnectionReport.FromHealth(result, _time.GetUtcNow()));
	}

	private void Record(ConnectionReport report)
	{
		LastReport = report;
		Raise(ReportUpdated, report, nameof(ReportUpdated));
		switch (_state.Observe(report.EssentialOk))
		{
			case ConnectionTransition.Lost:
				_log.Warn(Category, $"lost backend={report.Backend} language={report.Language} voice={report.Voice} code={report.ErrorCode ?? "-"}");
				Raise(Lost, report, nameof(Lost));
				break;
			case ConnectionTransition.Restored:
				_log.Info(Category, "restored");
				Raise(Restored, report, nameof(Restored));
				break;
			case ConnectionTransition.None:
				break;
		}
	}

	// Each subscriber runs on its own: one that throws must not keep the others (freeze, mic, speech) from hearing it.
	private void Raise(Action<ConnectionReport>? handlers, ConnectionReport report, string eventName)
	{
		if (handlers is null) return;
		foreach (var handler in handlers.GetInvocationList())
		{
			try
			{
				((Action<ConnectionReport>)handler)(report);
			}
			catch (Exception ex)
			{
				_log.Error(Category, $"{eventName} subscriber failed: {ex.GetType().Name}: {ex.Message}");
			}
		}
	}
}
