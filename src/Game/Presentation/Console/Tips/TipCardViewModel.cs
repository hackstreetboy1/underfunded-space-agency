using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Tips;

/// <summary>
/// The flight director's tip card (UI.md 3.3, GDD 9.1): stage pips, text, fact chips, dismiss, "Stronger hint",
/// the collapsed "Tip available" bar and a subtle cooldown line. A new tip (new id or stage) expands the card and
/// raises <see cref="NewTipShown"/> for the slide and chime; a dismissed tip stays collapsed until it changes.
/// Tips never pause or slow time.
/// </summary>
public sealed class TipCardViewModel : ViewModelBase
{
    public const int StageCount = 5;

    private readonly ConsoleWords _words;
    private readonly ObservableValue<TipCardState> _state = new(TipCardState.Hidden);
    private readonly ObservableValue<TipStage> _stage = new(TipStage.None);
    private readonly ObservableValue<StagePips> _pips = new(new StagePips(0, StageCount));
    private readonly ObservableValue<string> _stageWord = new(string.Empty);
    private readonly ObservableValue<string> _text = new(string.Empty);
    private readonly ObservableList<FactChip> _facts = new();
    private readonly ObservableValue<string?> _cooldownText = new();
    private readonly ObservableValue<bool> _canStrengthen = new(true);
    private readonly Signal<GuidanceTipSnapshot> _newTipShown = new();
    private readonly Signal<string> _dismissed = new();
    private readonly Signal<StrongerHintRequest> _strongerHintRequested = new();
    private readonly Signal<FactChip> _factHighlightRequested = new();
    private readonly List<FactChip> _factScratch = [];
    private GuidanceTipSnapshot? _tip;
    private (string Id, TipStage Stage)? _shownKey;

    public TipCardViewModel(ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
    }

    /// <summary>The flight director's signature under the tip.</summary>
    public string Signature => _words.TipSignature;

    /// <summary>Text of the collapsed bar.</summary>
    public string CollapsedText => _words.TipAvailable;

    public IReadOnlyObservable<TipCardState> State => _state;

    public IReadOnlyObservable<TipStage> Stage => _stage;

    public IReadOnlyObservable<StagePips> Pips => _pips;

    /// <summary><c>NUDGE</c> … <c>DIRECT</c>.</summary>
    public IReadOnlyObservable<string> StageWord => _stageWord;

    public IReadOnlyObservable<string> Text => _text;

    public ObservableList<FactChip> Facts => _facts;

    /// <summary><c>Next tip in 4 min</c> while the cooldown runs; otherwise null.</summary>
    public IReadOnlyObservable<string?> CooldownText => _cooldownText;

    /// <summary>"Stronger hint" is enabled (not already at the direct solution).</summary>
    public IReadOnlyObservable<bool> CanStrengthen => _canStrengthen;

    public Signal<GuidanceTipSnapshot> NewTipShown => _newTipShown;

    /// <summary>Carries the dismissed tip's id (the guidance director starts its cooldown).</summary>
    public Signal<string> Dismissed => _dismissed;

    public Signal<StrongerHintRequest> StrongerHintRequested => _strongerHintRequested;

    public Signal<FactChip> FactHighlightRequested => _factHighlightRequested;

    public void Apply(GuidanceTipSnapshot? tip, MissionTime? cooldownUntil, MissionTime now)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _tip = tip;
        _cooldownText.Value = cooldownUntil is { } until && until > now
            ? _words.NextTip(ReadoutFormatter.FormatRelative(now, until))
            : null;
        _canStrengthen.Value = tip is null || tip.Stage < TipStage.Direct;

        if (tip is null)
        {
            _shownKey = null;
            _state.Value = TipCardState.Hidden;
            _stage.Value = TipStage.None;
            _pips.Value = new StagePips(0, StageCount);
            _stageWord.Value = string.Empty;
            _text.Value = string.Empty;
            _facts.Clear();
            return;
        }

        _stage.Value = tip.Stage;
        _pips.Value = new StagePips(Math.Clamp((int)tip.Stage, 0, StageCount), StageCount);
        _stageWord.Value = _words.TipStageWord(tip.Stage);
        _text.Value = tip.Text;
        ApplyFacts(tip.Facts);

        var key = (tip.TipId, tip.Stage);
        if (_shownKey == key) return;
        _shownKey = key;
        _state.Value = TipCardState.Expanded;
        _newTipShown.Raise(tip);
    }

    /// <summary>✕: collapses to the "Tip available" bar.</summary>
    public bool Dismiss()
    {
        if (_state.Value != TipCardState.Expanded || _tip is not { } tip) return false;
        _state.Value = TipCardState.Collapsed;
        _dismissed.Raise(tip.TipId);
        return true;
    }

    /// <summary>Click on the collapsed bar.</summary>
    public bool Expand()
    {
        if (_state.Value != TipCardState.Collapsed) return false;
        _state.Value = TipCardState.Expanded;
        return true;
    }

    public bool RequestStrongerHint()
    {
        if (!_canStrengthen.Value) return false;
        _strongerHintRequested.Raise(new StrongerHintRequest(_tip?.TipId, _tip?.Stage ?? TipStage.None));
        return true;
    }

    public void HighlightFact(FactChip chip)
    {
        ArgumentNullException.ThrowIfNull(chip);
        _factHighlightRequested.Raise(chip);
    }

    private void ApplyFacts(IReadOnlyList<TipFactSnapshot> facts)
    {
        _factScratch.Clear();
        foreach (var fact in facts) _factScratch.Add(FactChip.From(fact, _words));
        if (_factScratch.SequenceEqual(_facts)) return;
        _facts.ResetTo(_factScratch);
    }
}
