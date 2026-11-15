using System.Globalization;
using Usa.Game.Content;
using Usa.Game.Services.Connection;

namespace Usa.Game.Flow.Screens;

public enum OverlayServiceState
{
    Ok,
    Reconnecting,
    Lost,
}

/// <param name="Label">"Ground link (local proxy)", "Voice" or "Language".</param>
public sealed record OverlayServiceLine(string Label, ServiceHealth Health, OverlayServiceState State, string StateText);

/// <summary>
/// Connection freeze overlay (UI.md 5, GDD 15.6): a full cover like the Esc menu with one status line per essential
/// service, a retry countdown and "Retry now". It never shows mission data. Visibility comes from the router
/// (<see cref="ScreenRouter.ConnectionOverlayVisible"/>), which the connection monitor's Lost/Restored events drive.
/// </summary>
public sealed class ConnectionOverlayModel
{
    private readonly ScreenRouter _router;
    private readonly StringTable _strings;

    public ConnectionOverlayModel(ScreenRouter router, StringTable strings)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(strings);
        _router = router;
        _strings = strings;
        Title = strings.Get("freezeOverlay.title");
        Body = strings.Get("freezeOverlay.body");
        RetryNowLabel = strings.Get("freezeOverlay.retryNow");
        NoDoubleExecution = strings.Get("freezeOverlay.noDoubleExecution");
        NotAFailure = strings.Get("freezeOverlay.notAFailure");
        Lines = BuildLines(ConnectionReport.Initial);
        Hint = HintFor(ConnectionReport.Initial);
    }

    public event Action? Changed;

    /// <summary>The host calls <c>BackendStatusTracker.Retry()</c> (which probes now, and restarts a proxy it launched if that died).</summary>
    public event Action? RetryRequested;

    public bool Visible => _router.ConnectionOverlayVisible;

    public string Title { get; }

    public string Body { get; }

    public string RetryNowLabel { get; }

    public string NoDoubleExecution { get; }

    public string NotAFailure { get; }

    public IReadOnlyList<OverlayServiceLine> Lines { get; private set; }

    /// <summary>What happened and what to do, in one line (start the proxy, check the connection, add a key); null once every service is back.</summary>
    public string? Hint { get; private set; }

    /// <summary>Feed every heartbeat report (<c>ConnectionMonitor.ReportUpdated</c>).</summary>
    public void Update(ConnectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Lines = BuildLines(report);
        Hint = HintFor(report);
        Changed?.Invoke();
    }

    /// <summary>"Retrying in 4 s" from <c>ConnectionMonitor.SecondsUntilNextProbe</c>.</summary>
    public string Countdown(double secondsUntilNextProbe)
    {
        var seconds = double.IsFinite(secondsUntilNextProbe) ? (int)Math.Ceiling(Math.Max(secondsUntilNextProbe, 0)) : 0;
        return _strings.Format("freezeOverlay.retryCountdown", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seconds"] = seconds.ToString(CultureInfo.InvariantCulture),
        });
    }

    public bool RetryNow()
    {
        if (!Visible) return false;
        RetryRequested?.Invoke();
        return true;
    }

    public static OverlayServiceState StateOf(ServiceHealth health) => health switch
    {
        ServiceHealth.Ok => OverlayServiceState.Ok,
        ServiceHealth.Unknown => OverlayServiceState.Reconnecting,
        _ => OverlayServiceState.Lost,
    };

    private string? HintFor(ConnectionReport report)
    {
        if (report.Backend == ServiceHealth.Unknown) return _strings.Get("freezeOverlay.hintChecking");
        if (report.Backend != ServiceHealth.Ok) return _strings.Get("freezeOverlay.hintProxyDown");

        var unconfigured = BackendStatusModel.FailingServices(report, _strings, health => health == ServiceHealth.Unconfigured);
        if (unconfigured.Length > 0) return Fill("freezeOverlay.hintUnconfigured", unconfigured);

        var down = BackendStatusModel.FailingServices(report, _strings, BackendStatusModel.IsProviderFailure);
        return down.Length > 0 ? Fill("freezeOverlay.hintServiceDown", down) : null;
    }

    private string Fill(string key, string services) =>
        _strings.Format(key, new Dictionary<string, string>(StringComparer.Ordinal) { ["services"] = services });

    private OverlayServiceLine[] BuildLines(ConnectionReport report) =>
    [
        Line("freezeOverlay.serviceBackend", report.Backend),
        Line("freezeOverlay.serviceVoice", report.Voice),
        Line("freezeOverlay.serviceLanguage", report.Language),
    ];

    private OverlayServiceLine Line(string labelKey, ServiceHealth health)
    {
        var state = StateOf(health);
        var text = state switch
        {
            OverlayServiceState.Ok => _strings.Get("freezeOverlay.stateOk"),
            OverlayServiceState.Reconnecting => _strings.Get("freezeOverlay.stateReconnecting"),
            _ => _strings.Get("freezeOverlay.stateLost"),
        };
        return new OverlayServiceLine(_strings.Get(labelKey), health, state, text);
    }
}
