using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Systems;

/// <summary>
/// A load-list row (UI.md 3.9): name, priority group chip, W, heat W, condition, on/off state, switch and the
/// "what depends on this" tooltip.
/// </summary>
public sealed class LoadRowViewModel : ViewModelBase
{
    private readonly ObservableValue<string> _name = new(string.Empty);
    private readonly ObservableValue<string> _priorityWord = new(string.Empty);
    private readonly ObservableValue<string> _powerText = new(string.Empty);
    private readonly ObservableValue<string> _heatText = new(string.Empty);
    private readonly StatusLanguage _status;
    private readonly ConsoleWords _words;
    private readonly ObservableValue<StatusVisual> _condition;
    private readonly ObservableValue<string> _stateWord;
    private readonly ObservableValue<string> _dependentsText = new(string.Empty);

    /// <remarks>The switch exists if the first snapshot has one; later snapshots update it but never add or remove it.</remarks>
    public LoadRowViewModel(LoadRowSnapshot snapshot, ControlSignals signals, StatusLanguage status, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(words);
        _status = status;
        _words = words;
        _stateWord = new(words.SwitchUnknown);
        _condition = new(status.Unknown);
        Id = snapshot.Id;
        if (snapshot.Switch is { } control) Switch = Track(new RemoteControlViewModel(control, signals, words));
        Apply(snapshot);
    }

    public ComponentId Id { get; }

    public IReadOnlyObservable<string> Name => _name;

    /// <summary><c>SURVIVAL</c>, <c>RETURN</c>, <c>SUPPORT</c>, <c>DISCRETIONARY</c>.</summary>
    public IReadOnlyObservable<string> PriorityWord => _priorityWord;

    /// <summary><c>120 W</c>.</summary>
    public IReadOnlyObservable<string> PowerText => _powerText;

    /// <summary>Heat output, <c>95.0 W</c>.</summary>
    public IReadOnlyObservable<string> HeatText => _heatText;

    public IReadOnlyObservable<StatusVisual> Condition => _condition;

    /// <summary><c>ON</c>, <c>OFF</c> or <c>UNKNOWN</c>.</summary>
    public IReadOnlyObservable<string> StateWord => _stateWord;

    /// <summary>Tooltip: <c>Depends on this: Cabin fan, Radio</c>.</summary>
    public IReadOnlyObservable<string> DependentsText => _dependentsText;

    public RemoteControlViewModel? Switch { get; }

    public void Apply(LoadRowSnapshot snapshot) => Apply(snapshot, null);

    /// <param name="now">MET of the snapshot, for the switch's <c>set OFF at 06:50</c> sub-line.</param>
    public void Apply(LoadRowSnapshot snapshot, MissionTime? now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (snapshot.Id != Id) throw new ArgumentException($"Snapshot for {snapshot.Id} applied to load {Id}.", nameof(snapshot));

        _name.Value = snapshot.Name;
        _priorityWord.Value = _words.LoadPriorityWord(snapshot.Priority);
        _powerText.Value = ReadoutFormatter.FormatQuantity(snapshot.PowerW, "W");
        _heatText.Value = ReadoutFormatter.FormatQuantity(snapshot.HeatW, "W");
        _condition.Value = _status.For(snapshot.Condition);
        _stateWord.Value = (snapshot.IsOn ?? snapshot.Switch?.IsOn) switch
        {
            true => _words.SwitchOn,
            false => _words.SwitchOff,
            null => _words.SwitchUnknown,
        };
        _dependentsText.Value = snapshot.Dependents.Count == 0
            ? _words.NothingDepends
            : _words.DependsOn(string.Join(", ", snapshot.Dependents));
        if (Switch is not null && snapshot.Switch is { } control) Switch.Apply(control, now);
    }
}
