using Godot;
using Usa.Game.UI.Theme;
using Usa.Game.Presentation.Console.Tips;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;

namespace Usa.Game.UI.Console.Regions;

/// <summary>
/// The flight director's tip card (<c>TipCard.tscn</c>, Figma G1-03 node 13:281; UI.md 3.3): signature, stage pips, stage
/// word, dismiss, text, fact chips, "Stronger hint", cooldown, and the 32 px "Tip available" bar when dismissed.
/// </summary>
public sealed partial class TipCard : PanelContainer
{
    private readonly BindingScope _bindings = new();
    private Control _expanded = null!;
    private Button _collapsed = null!;
    private Label _signature = null!;
    private HBoxContainer _pips = null!;
    private Label _stageWord = null!;
    private Label _text = null!;
    private HFlowContainer _facts = null!;
    private Button _stronger = null!;
    private Label _cooldown = null!;
    private TipCardViewModel? _tip;
    private int _expandedMinPx;
    private Button _dismiss = null!;
    private TipCardState _state = TipCardState.Hidden;
    private bool _layoutCollapses;
    private bool _openedInCompact;

    [Export]
    public PackedScene? FactChipScene { get; set; }

    public override void _Ready()
    {
        _expanded = GetNode<Control>("%Expanded");
        _collapsed = GetNode<Button>("%Collapsed");
        _signature = GetNode<Label>("%Signature");
        _pips = GetNode<HBoxContainer>("%Pips");
        _stageWord = GetNode<Label>("%StageWord");
        _text = GetNode<Label>("%Text");
        _facts = GetNode<HFlowContainer>("%Facts");
        _stronger = GetNode<Button>("%Stronger");
        _cooldown = GetNode<Label>("%Cooldown");
        _dismiss = GetNode<Button>("%Dismiss");
        StatusIconView.UseIcon(_dismiss, UiIcons.ActionClose);
        _dismiss.Pressed += () =>
        {
            _openedInCompact = false;
            _tip?.Dismiss();
        };
        _collapsed.Pressed += OnCollapsedPressed;
        _stronger.Pressed += () => _tip?.RequestStrongerHint();
        Visible = false;
    }

    public override void _ExitTree() => Unbind();

    public void Bind(TipCardViewModel tip)
    {
        ArgumentNullException.ThrowIfNull(tip);
        Unbind();
        _tip = tip;
        _signature.Text = tip.Signature;
        _collapsed.Text = tip.CollapsedText;
        _bindings.Observe(tip.State, state =>
        {
            if (state != _state && state == TipCardState.Expanded) _openedInCompact = false;
            _state = state;
            ShowState();
        });
        _bindings.Observe(tip.Pips, ShowPips);
        _bindings.Observe(tip.StageWord, _stageWord.ShowText);
        _bindings.Observe(tip.Text, text => _text.Text = text);
        _bindings.Observe(tip.CanStrengthen, can => _stronger.Disabled = !can);
        _bindings.Observe(tip.CooldownText, _cooldown.ShowText);
        if (FactChipScene is { } chipScene)
        {
            _bindings.Mirror(tip.Facts, new ListBinder<FactChip, FactChipButton>(_facts, chipScene, (view, chip) => view.Bind(chip, tip.HighlightFact)));
        }
    }

    public void Unbind()
    {
        _bindings.Release();
        _tip = null;
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _stronger.Text = labels.TipStronger;
        _stronger.TooltipText = labels.TipNoGradeNote;
        _dismiss.TooltipText = labels.TipDismiss;
    }

    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _expandedMinPx = layout.TipCardMinPx;
        _layoutCollapses = layout.CollapseTipCard;
        ShowState();
    }

    // The compact layout (Figma 21:807) shows a new tip as the 32 px "Tip available" bar; a click opens it there without
    // telling guidance anything, since the player has not dismissed it.
    private void ShowState()
    {
        var expanded = _state == TipCardState.Expanded && (!_layoutCollapses || _openedInCompact);
        Visible = _state != TipCardState.Hidden;
        _expanded.Visible = expanded;
        _collapsed.Visible = _state != TipCardState.Hidden && !expanded;
        CustomMinimumSize = new Vector2(0, expanded ? _expandedMinPx : 0);
    }

    private void OnCollapsedPressed()
    {
        if (_state == TipCardState.Expanded)
        {
            _openedInCompact = true;
            ShowState();
            return;
        }
        _tip?.Expand();
    }

    // The scene holds TipCardViewModel.StageCount pip panels.
    private void ShowPips(StagePips pips)
    {
        var count = _pips.GetChildCount();
        for (var i = 0; i < count; i++)
        {
            if (_pips.GetChild(i) is not Control pip) continue;
            pip.Visible = i < pips.Total;
            pip.SetVariation(ThemeVariations.Pip(i < pips.Filled));
        }
    }
}
