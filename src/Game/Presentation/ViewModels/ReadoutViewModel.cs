using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.ViewModels;

/// <summary>
/// One labelled value on the console (resource tile, systems-board row). Presenters push
/// <see cref="ReadoutSnapshot"/>s from simulation events; controls bind <see cref="Text"/>.
/// With a throttle, notifications are limited to <see cref="DefaultMinInterval"/> (10 Hz, UI.md 6.4).
/// </summary>
public sealed class ReadoutViewModel : ViewModelBase
{
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromMilliseconds(100);

    private readonly ObservableValue<ReadoutSnapshot> _snapshot;
    private readonly ObservableValue<ReadoutText> _text;
    private ReadoutSnapshot _shown;
    private MissionTime? _now;

    /// <param name="throttle">Null for immediate notifications (tests, rarely changing values).</param>
    /// <param name="minInterval">Defaults to <see cref="DefaultMinInterval"/> when a throttle is given.</param>
    public ReadoutViewModel(string label, string unit, UpdateThrottle? throttle = null, TimeSpan? minInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(unit);
        Label = label;
        var initial = ReadoutSnapshot.UnknownIn(unit);
        _snapshot = throttle is null
            ? new ObservableValue<ReadoutSnapshot>(initial)
            : new ObservableValue<ReadoutSnapshot>(initial, throttle, minInterval ?? DefaultMinInterval);
        _shown = initial;
        _text = new ObservableValue<ReadoutText>(ReadoutFormatter.Format(initial));
        Observe(_snapshot, snapshot =>
        {
            _shown = snapshot;
            _text.Value = ReadoutFormatter.Format(snapshot, _now);
        }, invokeImmediately: false);
    }

    public string Label { get; }

    public IReadOnlyObservable<ReadoutSnapshot> Snapshot => _snapshot;

    /// <summary>Formatted strings; changes only when the published snapshot formats differently.</summary>
    public IReadOnlyObservable<ReadoutText> Text => _text;

    /// <returns>True when the snapshot changed.</returns>
    public bool Update(ReadoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return _snapshot.Set(snapshot);
    }

    /// <summary>
    /// Tells the readout the current MET so a stale badge's age is derived from the observation time instead of
    /// freezing until the next push. The host calls it at its 10 Hz pump; <see cref="Text"/> notifies only when the
    /// shown age actually changes.
    /// </summary>
    public void SetMissionTime(MissionTime now)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _now = now;
        _text.Value = ReadoutFormatter.Format(_shown, now);
    }
}
