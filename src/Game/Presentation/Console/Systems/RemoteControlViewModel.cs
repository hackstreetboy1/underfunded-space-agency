using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Systems;

/// <summary>
/// A switch or button on the systems board (UI.md 3.9, GDD 12.3).
/// <list type="bullet">
/// <item>Remote, unguarded, low impact: <see cref="Toggle"/> or <see cref="Set"/> raises <see cref="ControlSignals.ExecuteRequested"/>.</item>
/// <item>Remote, guarded: <see cref="OpenGuard"/>, then <see cref="Toggle"/>/<see cref="Set"/> raises
/// <see cref="ControlSignals.ConfirmationRequested"/>; <see cref="Confirm"/> executes with <c>Confirmed</c> set,
/// <see cref="Cancel"/> drops it. Either closes the cover. A high-impact change of a switch without a cover (an action
/// whose parameter rules raise its impact) goes through the same dialog without the cover.</item>
/// <item>Crew-only: greyed with a crew-action icon; <see cref="Request"/> raises
/// <see cref="ControlSignals.CrewOrderRequested"/> with a pre-filled order.</item>
/// </list>
/// The snapshot stays authoritative: positions change only when the simulation reports them. <see cref="SubLine"/> reads
/// <c>Remote · set OFF at 06:50</c> once the ground has seen the position change.
/// </summary>
public sealed class RemoteControlViewModel : ViewModelBase
{
    private const string Separator = ReadoutFormatter.MetaSeparator;

    private readonly ControlSignals _signals;
    private readonly ConsoleWords _words;
    private readonly ObservableValue<string> _label = new(string.Empty);
    private readonly ObservableValue<bool?> _isOn = new();
    private readonly ObservableValue<string> _stateWord;
    private readonly ObservableValue<bool> _isCrewOnly = new();
    private readonly ObservableValue<bool> _isEnabled = new();
    private readonly ObservableValue<string?> _disabledReason = new();
    private readonly ObservableValue<GuardState> _guard = new(GuardState.NotGuarded);
    private readonly ObservableValue<bool> _canOperate = new();
    private readonly ObservableValue<ConfirmationRequest?> _pending = new();
    private readonly ObservableValue<string> _subLine = new(string.Empty);
    private ControlSnapshot _snapshot;
    private (bool On, MissionTime At)? _lastChange;

    public RemoteControlViewModel(ControlSnapshot snapshot, ControlSignals signals, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(words);
        ComponentId = snapshot.ComponentId;
        _signals = signals;
        _words = words;
        _stateWord = new(words.SwitchUnknown);
        _snapshot = snapshot;
        Apply(snapshot);
    }

    public ComponentId ComponentId { get; }

    public ControlKind Kind => _snapshot.Kind;

    public IReadOnlyObservable<string> Label => _label;

    /// <summary>Switch position; null when unknown (and always for buttons).</summary>
    public IReadOnlyObservable<bool?> IsOn => _isOn;

    /// <summary><c>ON</c>/<c>OFF</c> (or the control's own labels), <c>UNKNOWN</c>; empty for buttons.</summary>
    public IReadOnlyObservable<string> StateWord => _stateWord;

    /// <summary>Show greyed with the "crew action" icon and the "Request…" affordance.</summary>
    public IReadOnlyObservable<bool> IsCrewOnly => _isCrewOnly;

    public IReadOnlyObservable<bool> IsEnabled => _isEnabled;

    public IReadOnlyObservable<string?> DisabledReason => _disabledReason;

    public IReadOnlyObservable<GuardState> Guard => _guard;

    /// <summary>The switch itself can be clicked now (remote, enabled, cover open if guarded, position known).</summary>
    public IReadOnlyObservable<bool> CanOperate => _canOperate;

    /// <summary>The change waiting for the confirmation dialog; null otherwise.</summary>
    public IReadOnlyObservable<ConfirmationRequest?> PendingConfirmation => _pending;

    /// <summary>
    /// The line under the label (Figma G1-03): <c>Remote · set OFF at 06:50</c>, <c>Remote · guarded</c>,
    /// <c>Crew action only</c>, with the disabled reason appended.
    /// </summary>
    public IReadOnlyObservable<string> SubLine => _subLine;

    /// <summary>Applies a snapshot without a time: a position change is not dated.</summary>
    public void Apply(ControlSnapshot snapshot) => Apply(snapshot, null);

    /// <param name="now">MET of the snapshot; a position change seen here is shown as <c>set OFF at 06:50</c>.</param>
    public void Apply(ControlSnapshot snapshot, MissionTime? now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (snapshot.ComponentId != ComponentId)
            throw new ArgumentException($"Snapshot for {snapshot.ComponentId} applied to control {ComponentId}.", nameof(snapshot));

        var previous = _snapshot;
        _snapshot = snapshot;
        if (now is { } at && snapshot.Kind == ControlKind.Switch && previous.IsOn is { } before && snapshot.IsOn is { } after && before != after)
            _lastChange = (after, at);
        if (snapshot.IsOn is null) _lastChange = null;

        var remote = snapshot.Authority == ControlAuthority.Remote;
        _label.Value = snapshot.Label;
        _isOn.Value = snapshot.Kind == ControlKind.Switch ? snapshot.IsOn : null;
        _stateWord.Value = StateWordOf(snapshot);
        _isCrewOnly.Value = !remote;
        _isEnabled.Value = snapshot.Enabled;
        _disabledReason.Value = snapshot.Enabled ? null : snapshot.DisabledReason;

        if (!remote || !snapshot.Enabled) _pending.Value = null;
        if (!remote || !snapshot.Guarded) _guard.Value = _pending.Value is null ? GuardState.NotGuarded : GuardState.AwaitingConfirmation;
        else if (_guard.Value == GuardState.NotGuarded || !snapshot.Enabled) _guard.Value = GuardState.Closed;
        RefreshCanOperate();
        _subLine.Value = BuildSubLine();
    }

    /// <summary>Opens the hinged cover of a guarded remote switch.</summary>
    public bool OpenGuard()
    {
        if (_guard.Value != GuardState.Closed || !IsOperable()) return false;
        _guard.Value = GuardState.Open;
        RefreshCanOperate();
        return true;
    }

    public bool CloseGuard()
    {
        if (_guard.Value != GuardState.Open) return false;
        _guard.Value = GuardState.Closed;
        RefreshCanOperate();
        return true;
    }

    /// <summary>Flips a switch of known position, or presses a button.</summary>
    public bool Toggle()
    {
        if (_snapshot.Kind == ControlKind.Button) return Set(DesiredControlState.Activate);
        return _snapshot.IsOn is { } on && Set(on ? DesiredControlState.Off : DesiredControlState.On);
    }

    /// <summary>Requests a position (or <see cref="DesiredControlState.Activate"/> for a button).</summary>
    /// <returns>False when the control cannot do that now (crew-only, disabled, cover closed, already there).</returns>
    public bool Set(DesiredControlState desired)
    {
        if (!IsOperable() || !Fits(desired) || IsAlreadyAt(desired)) return false;

        var intent = new RemoteControlIntent(ComponentId, desired, Confirmed: false);
        var impact = ImpactOf(desired);
        switch (_guard.Value)
        {
            case GuardState.NotGuarded when !impact.RequiresConfirmation:
                _signals.ExecuteRequested.Raise(intent);
                return true;
            case GuardState.NotGuarded:
            case GuardState.Open:
                var request = new ConfirmationRequest(intent, ActionText(desired), _snapshot.Effects)
                {
                    EffectTextKeys = impact.EffectTextKeys,
                    Irreversible = impact.Irreversible,
                    ExecutorName = _words.ControlExecutorRemote,
                };
                _pending.Value = request;
                _guard.Value = GuardState.AwaitingConfirmation;
                RefreshCanOperate();
                _signals.ConfirmationRequested.Raise(request);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The confirmation dialog's Confirm: executes the pending change and closes the cover.</summary>
    public bool Confirm()
    {
        if (_guard.Value != GuardState.AwaitingConfirmation || _pending.Value is not { } request) return false;
        _pending.Value = null;
        _guard.Value = RestingGuard();
        RefreshCanOperate();
        _signals.ExecuteRequested.Raise(request.Intent with { Confirmed = true });
        return true;
    }

    /// <summary>The dialog's Cancel (or its 20 s timeout): nothing happens and the cover closes.</summary>
    public bool Cancel()
    {
        if (_guard.Value != GuardState.AwaitingConfirmation) return false;
        _pending.Value = null;
        _guard.Value = RestingGuard();
        RefreshCanOperate();
        return true;
    }

    /// <summary>"Request…" on a crew-only control.</summary>
    /// <param name="desired">Defaults to the opposite of a known position; with an unknown position the draft asks
    /// the crew to report it.</param>
    public bool Request(DesiredControlState? desired = null)
    {
        if (_snapshot.Authority != ControlAuthority.CrewOnly) return false;
        desired ??= _snapshot.Kind == ControlKind.Button
            ? DesiredControlState.Activate
            : _snapshot.IsOn is { } on ? (on ? DesiredControlState.Off : DesiredControlState.On) : (DesiredControlState?)null;
        if (desired is { } d && !Fits(d)) return false;

        var text = desired is { } target ? ActionText(target) : _words.ReportPosition(_snapshot.Label);
        _signals.CrewOrderRequested.Raise(new CrewOrderDraft(ComponentId, desired, text));
        return true;
    }

    private ControlImpact ImpactOf(DesiredControlState desired) => desired == DesiredControlState.Off ? _snapshot.OffImpact : _snapshot.OnImpact;

    private GuardState RestingGuard() =>
        _snapshot.Authority == ControlAuthority.Remote && _snapshot.Guarded ? GuardState.Closed : GuardState.NotGuarded;

    private bool IsOperable() => _snapshot.Authority == ControlAuthority.Remote && _snapshot.Enabled;

    private bool Fits(DesiredControlState desired) =>
        _snapshot.Kind == ControlKind.Button ? desired == DesiredControlState.Activate : desired != DesiredControlState.Activate;

    private bool IsAlreadyAt(DesiredControlState desired) =>
        _snapshot.Kind == ControlKind.Switch && _snapshot.IsOn is { } on && on == (desired == DesiredControlState.On);

    private string ActionText(DesiredControlState desired) => desired switch
    {
        DesiredControlState.On => _words.SwitchAction(_snapshot.Label, OnWord(_snapshot)),
        DesiredControlState.Off => _words.SwitchAction(_snapshot.Label, OffWord(_snapshot)),
        _ => _snapshot.Label,
    };

    private void RefreshCanOperate() =>
        _canOperate.Value = IsOperable()
            && _guard.Value is GuardState.NotGuarded or GuardState.Open
            && (_snapshot.Kind == ControlKind.Button || _snapshot.IsOn is not null);

    private string BuildSubLine()
    {
        var line = _snapshot.Authority == ControlAuthority.CrewOnly ? _words.ControlCrewOnly : _words.ControlRemote;
        if (_snapshot.Authority == ControlAuthority.Remote)
        {
            if (_snapshot.Guarded) line += Separator + _words.ControlGuarded;
            if (_lastChange is { } change)
            {
                var position = change.On ? OnWord(_snapshot) : OffWord(_snapshot);
                line += Separator + _words.ControlSetAt(position, ReadoutFormatter.FormatMetShort(change.At));
            }
        }
        if (_disabledReason.Value is { Length: > 0 } reason) line += Separator + reason;
        return line;
    }

    private string StateWordOf(ControlSnapshot snapshot)
    {
        if (snapshot.Kind == ControlKind.Button) return string.Empty;
        return snapshot.IsOn switch
        {
            true => OnWord(snapshot),
            false => OffWord(snapshot),
            null => _words.SwitchUnknown,
        };
    }

    private string OnWord(ControlSnapshot snapshot) => string.IsNullOrWhiteSpace(snapshot.OnLabel) ? _words.SwitchOn : snapshot.OnLabel;

    private string OffWord(ControlSnapshot snapshot) => string.IsNullOrWhiteSpace(snapshot.OffLabel) ? _words.SwitchOff : snapshot.OffLabel;
}
