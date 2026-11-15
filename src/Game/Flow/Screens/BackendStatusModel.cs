using Usa.Game.Content;
using Usa.Game.Flow.Backend;
using Usa.Game.Services.Connection;

namespace Usa.Game.Flow.Screens;

/// <summary>
/// The ground-link line on the main menu and the briefing: one headline, an optional "what to do" line, Retry when it
/// helps, and whether "Begin mission" may be pressed (GDD 15.6: the mission waits for the essential services). Bind to
/// <see cref="Changed"/>; call <see cref="Detach"/> when the screen goes away.
/// </summary>
public sealed class BackendStatusModel
{
    private readonly BackendStatusTracker _tracker;
    private readonly StringTable _strings;

    public BackendStatusModel(BackendStatusTracker tracker, StringTable strings)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(strings);
        _tracker = tracker;
        _strings = strings;
        RetryLabel = strings.Get("mainMenu.linkRetry");
        MissionWaitsLine = strings.Get("mainMenu.linkMissionWaits");
        (Headline, Detail) = Describe(tracker.Current);
        _tracker.Changed += OnChanged;
    }

    public event Action? Changed;

    public BackendStatus Status => _tracker.Current;

    public BackendState State => Status.State;

    public string Headline { get; private set; }

    /// <summary>How to fix it, in one line; null when nothing needs doing.</summary>
    public string? Detail { get; private set; }

    public string RetryLabel { get; }

    public bool ShowRetry => State is BackendState.NotRunning or BackendState.ServiceProblem;

    public bool MissionMayStart => Status.MissionMayStart;

    /// <summary>Shown under a disabled "Begin mission" (or Continue, Replay, Retry) while <see cref="MissionMayStart"/> is false.</summary>
    public string MissionWaitsLine { get; }

    public bool Retry() => ShowRetry && _tracker.Retry();

    public void Detach() => _tracker.Changed -= OnChanged;

    /// <summary>Names of the failing providers as the player knows them ("Voice", "Language"), comma separated.</summary>
    public static string FailingServices(ConnectionReport report, StringTable strings, Func<ServiceHealth, bool> failing)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(failing);
        var names = new List<string>(2);
        if (failing(report.Voice)) names.Add(strings.Get("freezeOverlay.serviceVoice"));
        if (failing(report.Language)) names.Add(strings.Get("freezeOverlay.serviceLanguage"));
        return string.Join(", ", names);
    }

    private void OnChanged(BackendStatus status)
    {
        (Headline, Detail) = Describe(status);
        Changed?.Invoke();
    }

    private (string Headline, string? Detail) Describe(BackendStatus status) => status.State switch
    {
        BackendState.Connected => (_strings.Get("mainMenu.linkConnected"), null),
        BackendState.FakeMode => (_strings.Get("mainMenu.linkFakeMode"), null),
        BackendState.Starting => (_strings.Get("mainMenu.linkStarting"), null),
        BackendState.NotRunning => (_strings.Get("mainMenu.linkNotRunning"),
            _strings.Get(status.LaunchFailed ? "mainMenu.linkLaunchFailed" : "mainMenu.linkHowToStart")),
        BackendState.ServiceProblem => DescribeProblem(status.Report),
        _ => (_strings.Get("mainMenu.linkChecking"), null),
    };

    // A missing key needs a different fix than a network problem, so it wins when both apply.
    private (string, string?) DescribeProblem(ConnectionReport report)
    {
        var unconfigured = FailingServices(report, _strings, health => health == ServiceHealth.Unconfigured);
        if (unconfigured.Length > 0)
            return (Fill("mainMenu.linkUnconfigured", unconfigured), _strings.Get("mainMenu.linkUnconfiguredHint"));

        // The proxy itself answered with an error: the whole link is the problem.
        var down = report.Backend == ServiceHealth.Ok
            ? FailingServices(report, _strings, IsProviderFailure)
            : _strings.Get("freezeOverlay.serviceBackend");
        return (Fill("mainMenu.linkServiceDown", down), _strings.Get("mainMenu.linkServiceDownHint"));
    }

    internal static bool IsProviderFailure(ServiceHealth health) => health is not (ServiceHealth.Ok or ServiceHealth.Unknown);

    private string Fill(string key, string services) =>
        _strings.Format(key, new Dictionary<string, string>(StringComparer.Ordinal) { ["services"] = services });
}
