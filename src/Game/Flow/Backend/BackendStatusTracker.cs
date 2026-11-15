using Usa.Game.Services.Ai.Contracts;
using Usa.Game.Services.Connection;

namespace Usa.Game.Flow.Backend;

/// <summary>
/// Turns connection heartbeats into the pre-mission <see cref="BackendStatus"/> and drives the optional launcher: when the
/// first heartbeat finds nothing on the proxy address it starts the proxy once (if the <see cref="IBackendStarter"/> can),
/// then waits up to <see cref="StartTimeout"/> while the host calls <see cref="Tick"/> every second. Once the link has been
/// up, it takes <see cref="BadReportsToDrop"/> bad heartbeats in a row to report a problem, so a single blip never flickers
/// the menu. Pure and main-thread only; the host feeds <see cref="Observe"/> from <c>ConnectionMonitor.ReportUpdated</c>.
/// </summary>
public sealed class BackendStatusTracker
{
    public const int BadReportsToDrop = 2;

    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly Action _requestProbe;
    private readonly IBackendStarter? _starter;
    private readonly TimeProvider _time;
    private bool _autoStartTried;
    private bool _wasUp;
    private int _badReports;
    private long _startedAt;

    /// <param name="requestProbe">Runs a heartbeat now (<c>ConnectionMonitor.ProbeNow</c>).</param>
    /// <param name="starter">Null when the build cannot start the proxy itself.</param>
    public BackendStatusTracker(Action requestProbe, IBackendStarter? starter = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(requestProbe);
        _requestProbe = requestProbe;
        _starter = starter;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised whenever <see cref="Current"/> changes.</summary>
    public event Action<BackendStatus>? Changed;

    public BackendStatus Current { get; private set; } = BackendStatus.Initial;

    /// <summary>Whether <see cref="Retry"/> can start the proxy (setting on, script present).</summary>
    public bool CanStartProxy => _starter?.CanStart == true;

    public void Observe(ConnectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.EssentialOk)
        {
            _wasUp = true;
            _badReports = 0;
            Set(new BackendStatus(report.Mode == ProviderMode.Fake ? BackendState.FakeMode : BackendState.Connected, report));
            return;
        }

        var reachable = report.Backend is not (ServiceHealth.Unreachable or ServiceHealth.Unknown);
        // Still booting: the launcher's Tick decides when to give up.
        if (!reachable && Current.State == BackendState.Starting) return;

        if (!reachable && !_autoStartTried && !_wasUp && _starter?.CanStart == true)
        {
            _autoStartTried = true;
            BeginStart(report);
            return;
        }

        // Once the link was up, a single bad heartbeat keeps the last good state.
        _badReports++;
        if (_wasUp && _badReports < BadReportsToDrop && Current.State is (BackendState.Connected or BackendState.FakeMode))
            return;

        Set(new BackendStatus(reachable ? BackendState.ServiceProblem : BackendState.NotRunning, report, LaunchFailed: !reachable && Current.LaunchFailed));
    }

    /// <summary>Call about once a second. While starting it polls the proxy and gives up when the process exits or times out.</summary>
    public void Tick()
    {
        if (Current.State != BackendState.Starting) return;
        if (_starter?.IsRunning != true || _time.GetElapsedTime(_startedAt) >= StartTimeout)
        {
            Set(new BackendStatus(BackendState.NotRunning, Current.Report, LaunchFailed: true));
            return;
        }
        _requestProbe();
    }

    /// <summary>
    /// The player's Retry: starts the proxy again when nothing answers and the starter allows it (and none it launched is
    /// still alive), then runs a heartbeat now. False while a start is already in progress.
    /// </summary>
    public bool Retry()
    {
        if (Current.State == BackendState.Starting) return false;
        if (Current.State == BackendState.NotRunning && _starter is { CanStart: true, IsRunning: false })
        {
            BeginStart(Current.Report);
            return true;
        }
        _requestProbe();
        return true;
    }

    private void BeginStart(ConnectionReport report)
    {
        if (_starter?.TryStart() != true)
        {
            Set(new BackendStatus(BackendState.NotRunning, report, LaunchFailed: true));
            return;
        }
        _startedAt = _time.GetTimestamp();
        Set(new BackendStatus(BackendState.Starting, report));
        _requestProbe();
    }

    private void Set(BackendStatus next)
    {
        if (next == Current) return;
        Current = next;
        Changed?.Invoke(next);
    }
}
