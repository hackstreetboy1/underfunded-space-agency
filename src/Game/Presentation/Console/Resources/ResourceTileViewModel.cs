using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>
/// One resource-strip tile (UI.md 3.2): label, value with knowledge badge, optional second value, trend arrow,
/// 60-minute sparkline and the click-through to its systems-board tab. Values pass through
/// <see cref="KnowledgeFilter"/>, and only fresh known values are plotted.
/// </summary>
public sealed class ResourceTileViewModel : ViewModelBase
{
    private readonly Signal<SystemsTab> _openTabRequested;
    private readonly SparklineSampler _sampler = new();
    private readonly ObservableValue<bool> _hasSecondary = new();
    private readonly ObservableValue<string?> _detail = new();
    private readonly ObservableValue<TrendIndicator> _trend;
    private readonly ObservableValue<SparklineSeries> _sparkline = new(SparklineSeries.Empty);
    private SystemsTab _tab;

    public ResourceTileViewModel(
        ResourceSnapshot initial,
        MissionTime now,
        Signal<AskDraft> askRequested,
        Signal<SystemsTab> openTabRequested,
        ConsoleWords words,
        UpdateThrottle? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(openTabRequested);
        Id = initial.Id;
        Label = initial.Label;
        _openTabRequested = openTabRequested;
        _trend = ConsoleObservables.Throttled(TrendIndicator.None, throttle);
        Primary = Track(new FactReadoutViewModel(
            initial.PrimaryFactKey ?? initial.Id, initial.Label, initial.Primary.Unit, askRequested, words, throttle));
        Secondary = Track(new FactReadoutViewModel(
            initial.SecondaryFactKey ?? initial.Id + ".secondary", initial.Label, initial.Secondary?.Unit ?? string.Empty, askRequested, words, throttle));
        Apply(initial, now);
    }

    public string Id { get; }

    public string Label { get; }

    public FactReadoutViewModel Primary { get; }

    /// <summary>Second value (e.g. percent); shown only while <see cref="HasSecondary"/>.</summary>
    public FactReadoutViewModel Secondary { get; }

    public IReadOnlyObservable<bool> HasSecondary => _hasSecondary;

    /// <summary>State word such as <c>SEALED</c>; null when none.</summary>
    public IReadOnlyObservable<string?> Detail => _detail;

    public IReadOnlyObservable<TrendIndicator> Trend => _trend;

    public IReadOnlyObservable<SparklineSeries> Sparkline => _sparkline;

    public SystemsTab Tab => _tab;

    public void Apply(ResourceSnapshot snapshot, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (snapshot.Id != Id) throw new ArgumentException($"Snapshot for '{snapshot.Id}' applied to tile '{Id}'.", nameof(snapshot));

        _tab = snapshot.Tab;
        _detail.Value = string.IsNullOrWhiteSpace(snapshot.Detail) ? null : snapshot.Detail;

        var primary = KnowledgeFilter.Sanitize(snapshot.Primary, now);
        Primary.Update(primary, now, snapshot.AskText);
        _trend.Value = TrendIndicator.From(primary);

        _hasSecondary.Value = snapshot.Secondary is not null;
        if (snapshot.Secondary is { } secondary) Secondary.Update(secondary, now, snapshot.AskText);

        if (_sampler.Sample(now, KnowledgeFilter.PlottableValue(primary))) _sparkline.Value = _sampler.Series;
    }

    /// <summary>Tile click: opens the matching systems-board tab.</summary>
    public void Open() => _openTabRequested.Raise(_tab);

    /// <summary>"Ask" on an unknown value.</summary>
    public bool Ask() => Primary.Ask() || (_hasSecondary.Value && Secondary.Ask());
}
