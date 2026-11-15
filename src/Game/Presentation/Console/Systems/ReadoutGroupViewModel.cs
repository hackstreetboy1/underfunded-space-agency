using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Systems;

/// <summary>A titled group of knowledge-filtered readouts on a systems-board tab.</summary>
public sealed class ReadoutGroupViewModel : ViewModelBase
{
    private readonly Signal<AskDraft> _askRequested;
    private readonly ConsoleWords _words;
    private readonly UpdateThrottle? _throttle;
    private readonly ObservableValue<string> _title = new(string.Empty);
    private readonly ObservableList<FactReadoutViewModel> _readouts = new();

    public ReadoutGroupViewModel(string id, Signal<AskDraft> askRequested, ConsoleWords words, UpdateThrottle? throttle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(askRequested);
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
        Id = id;
        _askRequested = askRequested;
        _throttle = throttle;
    }

    public string Id { get; }

    public IReadOnlyObservable<string> Title => _title;

    public ObservableList<FactReadoutViewModel> Readouts => _readouts;

    public void Apply(ReadoutGroupSnapshot group, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(group);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _title.Value = group.Title;
        KeyedSync.Sync(
            _readouts,
            group.Readouts,
            static readout => readout.Key,
            static readout => readout.Key,
            readout =>
            {
                var created = new FactReadoutViewModel(readout.Key, readout.Label, readout.Value.Unit, _askRequested, _words, _throttle);
                created.Update(readout.Value, now, readout.AskText, readout.Gauge);
                return created;
            },
            (existing, readout) =>
            {
                existing.Update(readout.Value, now, readout.AskText, readout.Gauge);
                return existing;
            });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            foreach (var readout in _readouts) readout.Dispose();
        }
        base.Dispose(disposing);
    }
}
