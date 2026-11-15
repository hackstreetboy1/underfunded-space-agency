using Usa.Game.Presentation.Console.Crew;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>
/// The resource strip (UI.md 3.2): tiles in snapshot order, the Crew tile's mini health bars and the forecast pill
/// with its breakdown popover.
/// </summary>
public sealed class ResourceStripViewModel : ViewModelBase
{
    private readonly UpdateThrottle? _throttle;
    private readonly ObservableList<ResourceTileViewModel> _tiles = new();
    private readonly ObservableList<CrewHealthBar> _crewHealth = new();
    private readonly ForecastText _forecastText;
    private readonly ConsoleWords _words;
    private readonly ObservableValue<ForecastPill> _forecast;
    private readonly ObservableValue<ForecastBreakdown?> _breakdown = new();
    private readonly ObservableValue<bool> _breakdownOpen = new();
    private readonly Signal<SystemsTab> _openTabRequested = new();
    private readonly List<CrewHealthBar> _crewScratch = [];
    private ForecastSnapshot? _lastForecast;

    /// <param name="forecastText">Forecast and status words.</param>
    /// <param name="throttle">Limits readouts and trends to 10 Hz; null for immediate notifications.</param>
    /// <param name="askRequested">Shared "Ask" sink; a private one when null.</param>
    public ResourceStripViewModel(
        ForecastText forecastText, ConsoleWords words, UpdateThrottle? throttle = null, Signal<AskDraft>? askRequested = null)
    {
        ArgumentNullException.ThrowIfNull(forecastText);
        ArgumentNullException.ThrowIfNull(words);
        _forecastText = forecastText;
        _words = words;
        _forecast = new(forecastText.Pill(null));
        _throttle = throttle;
        AskRequested = askRequested ?? new Signal<AskDraft>();
    }

    public ObservableList<ResourceTileViewModel> Tiles => _tiles;

    public ObservableList<CrewHealthBar> CrewHealth => _crewHealth;

    public IReadOnlyObservable<ForecastPill> Forecast => _forecast;

    /// <summary>Null until a forecast exists.</summary>
    public IReadOnlyObservable<ForecastBreakdown?> Breakdown => _breakdown;

    public IReadOnlyObservable<bool> BreakdownOpen => _breakdownOpen;

    /// <summary>A tile was clicked; the console selects that systems-board tab.</summary>
    public Signal<SystemsTab> OpenTabRequested => _openTabRequested;

    public Signal<AskDraft> AskRequested { get; }

    public ResourceTileViewModel? Find(string id)
    {
        foreach (var tile in _tiles)
        {
            if (tile.Id == id) return tile;
        }
        return null;
    }

    public void Apply(ConsoleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Apply(snapshot.Resources, snapshot.Forecast, snapshot.Crew, snapshot.Met);
    }

    public void Apply(
        IReadOnlyList<ResourceSnapshot> resources,
        ForecastSnapshot? forecast,
        IReadOnlyList<CrewMemberSnapshot> crew,
        MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(crew);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        KeyedSync.Sync(
            _tiles,
            resources,
            static resource => resource.Id,
            static tile => tile.Id,
            resource => new ResourceTileViewModel(resource, now, AskRequested, _openTabRequested, _words, _throttle),
            (tile, resource) =>
            {
                tile.Apply(resource, now);
                return tile;
            });

        _crewScratch.Clear();
        foreach (var member in crew) _crewScratch.Add(new CrewHealthBar(member.Id, member.Name, CrewHealthView.From(member, _forecastText.Status)));
        KeyedSync.SyncRows(_crewHealth, _crewScratch, static bar => bar.Id);

        ApplyForecast(forecast);
    }

    public void ToggleBreakdown() => _breakdownOpen.Value = !_breakdownOpen.Value && _breakdown.Value is not null;

    public void CloseBreakdown() => _breakdownOpen.Value = false;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            foreach (var tile in _tiles) tile.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ApplyForecast(ForecastSnapshot? forecast)
    {
        if (forecast is not null && forecast.Equals(_lastForecast)) return;
        _lastForecast = forecast;
        _forecast.Value = _forecastText.Pill(forecast);
        _breakdown.Value = forecast is null ? null : _forecastText.Breakdown(forecast);
        if (forecast is null) _breakdownOpen.Value = false;
    }
}
