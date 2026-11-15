using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Crew;

/// <summary>
/// One crew card (UI.md 3.4): name, role, health bar with percent and band word, activity line, speaking
/// indicator, non-nominal state icons and the Talk button. A deceased astronaut's card shows only the name and
/// <c>LOST · MET hh:mm</c> in <c>text.mid</c>; the feed fades to <c>bg.2</c>.
/// </summary>
public sealed class CrewCardViewModel : ViewModelBase
{
    private readonly Signal<CrewId> _talkRequested;
    private readonly ConsoleWords _words;
    private readonly ObservableValue<string> _name = new(string.Empty);
    private readonly ObservableValue<string> _role = new(string.Empty);
    private readonly StatusLanguage _status;
    private readonly ObservableValue<CrewHealthView> _health;
    private readonly ObservableValue<string> _activity;
    private readonly ObservableValue<bool> _isSpeaking = new();
    private readonly ObservableValue<bool> _isDeceased = new();
    private readonly ObservableValue<string?> _lostText = new();
    private readonly ObservableValue<string> _nameColourToken = new(ColourTokens.TextHi);
    private readonly ObservableValue<bool> _fatigued = new();
    private readonly ObservableValue<bool> _dehydrated = new();

    /// <param name="talkRequested">Shared sink for the Talk button (sets the comms addressee).</param>
    public CrewCardViewModel(CrewId id, Signal<CrewId> talkRequested, StatusLanguage status, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(talkRequested);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(words);
        _status = status;
        _words = words;
        _activity = new(words.Idle);
        _health = new(CrewHealthView.Unknown(status));
        Id = id;
        _talkRequested = talkRequested;
    }

    public CrewId Id { get; }

    public IReadOnlyObservable<string> Name => _name;

    public IReadOnlyObservable<string> Role => _role;

    public IReadOnlyObservable<CrewHealthView> Health => _health;

    /// <summary><c>Reading OAMS temps…</c>, <c>Idle</c>; empty once deceased.</summary>
    public IReadOnlyObservable<string> Activity => _activity;

    public IReadOnlyObservable<bool> IsSpeaking => _isSpeaking;

    public IReadOnlyObservable<bool> IsDeceased => _isDeceased;

    /// <summary><c>LOST · MET 06:58</c>; null while alive.</summary>
    public IReadOnlyObservable<string?> LostText => _lostText;

    public IReadOnlyObservable<string> NameColourToken => _nameColourToken;

    public IReadOnlyObservable<bool> ShowFatigueIcon => _fatigued;

    public IReadOnlyObservable<bool> ShowHydrationIcon => _dehydrated;

    public bool CanTalk => !_isDeceased.Value;

    public void Apply(CrewMemberSnapshot member)
    {
        ArgumentNullException.ThrowIfNull(member);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (member.Id != Id) throw new ArgumentException($"Snapshot for {member.Id} applied to the card of {Id}.", nameof(member));

        var health = CrewHealthView.From(member, _status);
        var deceased = health.Band == CrewHealthBand.Deceased;
        _name.Value = member.Name;
        _role.Value = member.Role;
        _health.Value = health;
        _isDeceased.Value = deceased;
        _nameColourToken.Value = deceased ? ColourTokens.TextMid : ColourTokens.TextHi;
        _lostText.Value = deceased ? LostLine(member.DiedAt) : null;
        _activity.Value = deceased ? string.Empty : string.IsNullOrWhiteSpace(member.Activity) ? _words.Idle : member.Activity;
        _isSpeaking.Value = !deceased && member.IsSpeaking;
        _fatigued.Value = !deceased && member.Fatigued;
        _dehydrated.Value = !deceased && member.Dehydrated;
    }

    /// <summary>Talk button: makes this astronaut the comms addressee.</summary>
    public bool Talk()
    {
        if (!CanTalk) return false;
        _talkRequested.Raise(Id);
        return true;
    }

    private string LostLine(MissionTime? diedAt) =>
        diedAt is { } at ? _words.LostAt(ReadoutFormatter.FormatMetShort(at)) : _words.Lost;
}
