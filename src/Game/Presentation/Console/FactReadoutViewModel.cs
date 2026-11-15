using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console;

/// <summary>
/// A <see cref="ReadoutViewModel"/> fed through <see cref="KnowledgeFilter"/>, with its knowledge icon and the
/// "Ask" affordance for unknown values. Used by resource tiles and systems-board readouts.
/// </summary>
public sealed class FactReadoutViewModel : ViewModelBase
{
    private readonly Signal<AskDraft> _askRequested;
    private readonly ConsoleWords _words;
    private readonly ObservableValue<KnowledgeVisual> _knowledge = new(KnowledgeBadges.Unknown);
    private readonly ObservableValue<ReadoutParts> _parts;
    private readonly ObservableValue<GaugeReading?> _gauge = new();
    private GaugeScale? _gaugeScale;
    private string? _askText;

    /// <param name="askRequested">Shared sink for "Ask" requests (the comms input pre-fill).</param>
    /// <param name="throttle">Null for immediate notifications.</param>
    public FactReadoutViewModel(
        string key, string label, string unit, Signal<AskDraft> askRequested, ConsoleWords words, UpdateThrottle? throttle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(askRequested);
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
        Key = key;
        _askRequested = askRequested;
        Readout = Track(new ReadoutViewModel(label, unit, throttle));
        _parts = new(ReadoutParts.From(Readout.Snapshot.Value));
        Observe(Readout.Snapshot, snapshot =>
        {
            _knowledge.Value = KnowledgeBadges.For(snapshot.Knowledge);
            _parts.Value = ReadoutParts.From(snapshot);
            if (_gaugeScale is { } scale) _gauge.Value = GaugeReading.From(snapshot, scale);
        }, invokeImmediately: false);
    }

    public string Key { get; }

    public string Label => Readout.Label;

    public ReadoutViewModel Readout { get; }

    /// <summary>Number and unit apart, for tiles and gauges that set the unit smaller (UI.md 6.2: the unit is always shown).</summary>
    public IReadOnlyObservable<ReadoutParts> Parts => _parts;

    /// <summary>The arc gauge of a readout drawn as one; null for plain readouts.</summary>
    public IReadOnlyObservable<GaugeReading?> Gauge => _gauge;

    /// <summary>The scale this readout's gauge is drawn on; null when it is a plain readout.</summary>
    public GaugeScale? GaugeScale => _gaugeScale;

    /// <summary>Icon and colours for the published knowledge state; the badge word is in <c>Readout.Text</c>.</summary>
    public IReadOnlyObservable<KnowledgeVisual> Knowledge => _knowledge;

    /// <summary>The value is unknown, so "Ask" is offered.</summary>
    public bool CanAsk => Readout.Snapshot.Value.Knowledge == ReadoutKnowledge.Unknown;

    /// <param name="now">Current MET, for the stale age.</param>
    /// <param name="askText">Pre-filled request; defaults to <c>Read &lt;label&gt;</c>.</param>
    /// <returns>True when the sanitized snapshot changed.</returns>
    public bool Update(ReadoutSnapshot snapshot, MissionTime now, string? askText = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return Update(snapshot, now, askText, _gaugeScale);
    }

    /// <param name="gauge">Draw as an arc gauge on this scale; null for a plain readout.</param>
    public bool Update(ReadoutSnapshot snapshot, MissionTime now, string? askText, GaugeScale? gauge)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!Equals(_gaugeScale, gauge))
        {
            _gaugeScale = gauge;
            _gauge.Value = gauge is null ? null : GaugeReading.From(Readout.Snapshot.Value, gauge);
        }
        _askText = askText;
        return Readout.Update(KnowledgeFilter.Sanitize(snapshot, now));
    }

    /// <summary>Raises a pre-filled request for this value.</summary>
    /// <returns>False when the value is not unknown.</returns>
    public bool Ask()
    {
        if (!CanAsk) return false;
        var text = string.IsNullOrWhiteSpace(_askText) ? _words.AskRead(Label) : _askText;
        _askRequested.Raise(new AskDraft(Key, Label, text));
        return true;
    }
}
