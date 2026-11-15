using Godot;
using Usa.Game.Flow;
using Usa.Game.Flow.Screens;
using Usa.Game.UI.Frontend.Model;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The Esc menu (<c>EscMenu.tscn</c>, Figma G6-06a/b/c; UI.md 5): an opaque <c>bg.0</c> cover over the frozen mission with
/// the agency mark, "Simulation frozen", the frozen MET (decision 5), Resume / Settings / Restart mission / Abandon to menu,
/// and the encyclopedia notice. Restart and Abandon open the Set 1 confirm dialog over a 40 % scrim; it never
/// auto-cancels while frozen (decision 4). Esc itself goes through <see cref="ScreenRouter.HandleEscape"/>.
/// </summary>
public sealed partial class EscMenuView : Control
{
    private ColorRect _cover = null!;
    private ColorRect _frozenMark = null!;
    private Label _mark = null!;
    private Label _missionLine = null!;
    private Label _title = null!;
    private Label _body = null!;
    private Label _met = null!;
    private Label _frozenTag = null!;
    private Button _resume = null!;
    private Button _settings = null!;
    private Button _restart = null!;
    private Button _abandon = null!;
    private Label _encyclopediaNote = null!;
    private Label _keysHint = null!;
    private Control _confirmLayer = null!;
    private Label _confirmTitle = null!;
    private PanelContainer _effects = null!;
    private Label _effectsTitle = null!;
    private Label _confirmPrompt = null!;
    private Label _frozenEffect = null!;
    private Label _noAutoCancel = null!;
    private Button _cancel = null!;
    private Button _confirm = null!;
    private FrontendContext? _context;
    private EscMenuModel? _model;
    private Func<long?> _frozenMetMs = () => null;
    private EscMenuState _shownState = EscMenuState.Closed;
    private bool _shownVisible;
    private Tween? _noticeTween;

    public override void _Ready()
    {
        _cover = GetNode<ColorRect>("%Cover");
        _frozenMark = GetNode<ColorRect>("%FrozenMark");
        _mark = GetNode<Label>("%Mark");
        _missionLine = GetNode<Label>("%MissionLine");
        _title = GetNode<Label>("%Title");
        _body = GetNode<Label>("%Body");
        _met = GetNode<Label>("%Met");
        _frozenTag = GetNode<Label>("%FrozenTag");
        _resume = GetNode<Button>("%Resume");
        _settings = GetNode<Button>("%Settings");
        _restart = GetNode<Button>("%Restart");
        _abandon = GetNode<Button>("%Abandon");
        _encyclopediaNote = GetNode<Label>("%EncyclopediaNote");
        _keysHint = GetNode<Label>("%KeysHint");
        _confirmLayer = GetNode<Control>("%ConfirmLayer");
        _confirmTitle = GetNode<Label>("%ConfirmTitle");
        _effects = GetNode<PanelContainer>("%Effects");
        _effectsTitle = GetNode<Label>("%EffectsTitle");
        _confirmPrompt = GetNode<Label>("%ConfirmPrompt");
        _frozenEffect = GetNode<Label>("%FrozenEffect");
        _noAutoCancel = GetNode<Label>("%NoAutoCancel");
        _cancel = GetNode<Button>("%Cancel");
        _confirm = GetNode<Button>("%Confirm");

        _resume.Pressed += () => _model?.Resume();
        _settings.Pressed += () => _model?.OpenSettings();
        _restart.Pressed += () => _model?.Restart();
        _abandon.Pressed += () => _model?.Abandon();
        _cancel.Pressed += () => _model?.Cancel();
        _confirm.Pressed += () => _model?.Confirm();
        _confirm.SetMeta(Presentation.Cues.UiSoundCue.MetaKey, "confirm");
        _confirmLayer.Visible = false;
    }

    // While a restart/abandon confirm is up Enter always confirms, whichever button has focus (Esc cancels via the router).
    public override void _Input(InputEvent @event)
    {
        if (_model is null || !Visible || !_confirmLayer.Visible) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }) return;
        _model.Confirm();
        GetViewport().SetInputAsHandled();
    }

    /// <param name="frozenMetMs">The session's MET while frozen; null shows no clock.</param>
    public void Initialize(FrontendContext context, Func<long?> frozenMetMs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frozenMetMs);
        _context = context;
        _frozenMetMs = frozenMetMs;
        _model = new EscMenuModel(context.Router, context.Strings);
        var text = context.Text;
        var tokens = context.Tokens;

        _cover.Color = tokens.ColorBg0;
        _frozenMark.Color = tokens.ColorInfo;
        _mark.Text = text.Get("agency.headerMark");
        _missionLine.Text = MissionLine(context);
        _title.Text = _model.Title;
        _body.Text = _model.Body;
        _frozenTag.Text = text.Get("escMenu.frozenTag");
        _resume.Text = _model.ResumeLabel;
        _settings.Text = _model.SettingsLabel;
        _restart.Text = _model.RestartLabel;
        _abandon.Text = _model.AbandonLabel;
        _encyclopediaNote.Text = _model.EncyclopediaUnavailable;
        _keysHint.Text = text.Get("escMenu.keysHint");
        _effectsTitle.Text = text.Get("escMenu.effectsTitle").ToUpperInvariant();
        _frozenEffect.Text = text.Get("escMenu.frozenEffect");
        _noAutoCancel.Text = text.Get("escMenu.noAutoCancel");
        _cancel.Text = _model.CancelLabel + "  Esc";
        _effects.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = tokens.ColorTintCaution,
            BorderColor = tokens.ColorCaution,
            BorderWidthLeft = tokens.BorderWidthPx,
            BorderWidthTop = tokens.BorderWidthPx,
            BorderWidthRight = tokens.BorderWidthPx,
            BorderWidthBottom = tokens.BorderWidthPx,
            CornerRadiusTopLeft = tokens.RadiusBadgePx,
            CornerRadiusTopRight = tokens.RadiusBadgePx,
            CornerRadiusBottomLeft = tokens.RadiusBadgePx,
            CornerRadiusBottomRight = tokens.RadiusBadgePx,
            ContentMarginLeft = tokens.Space12Px,
            ContentMarginRight = tokens.Space12Px,
            ContentMarginTop = tokens.Space12Px,
            ContentMarginBottom = tokens.Space12Px,
        });
        Refresh();
    }

    /// <summary>Follows the router: the confirm dialog, the frozen MET and focus. The shell calls it on every router change.</summary>
    public void Refresh()
    {
        if (_model is null || _context is null) return;
        var state = _model.State;
        var visible = Visible && _model.Visible;
        var confirming = state is EscMenuState.ConfirmRestart or EscMenuState.ConfirmAbandon;
        _confirmLayer.Visible = confirming;
        // The dialog is modal over the menu: Tab never reaches the buttons behind the scrim.
        foreach (var button in new[] { _resume, _settings, _restart, _abandon })
            button.FocusMode = confirming ? FocusModeEnum.None : FocusModeEnum.All;
        if (confirming)
        {
            var abandon = state == EscMenuState.ConfirmAbandon;
            _confirmTitle.Text = abandon ? _model.AbandonLabel : _model.RestartLabel;
            _confirmPrompt.Text = _model.ConfirmPrompt ?? string.Empty;
            _confirm.Text = _confirmTitle.Text + "  Enter";
            _confirm.ThemeTypeVariation = abandon ? Ui.ButtonDanger : Ui.ButtonPrimary;
        }

        if (state == _shownState && visible == _shownVisible) return;
        var opened = !_shownVisible;
        _shownState = state;
        _shownVisible = visible;
        if (!visible) return;

        if (opened) ShowMet();
        if (confirming) _confirm.GrabFocus();
        else _resume.GrabFocus();
    }

    /// <summary>F1 while frozen: the notice under the buttons is briefly emphasised.</summary>
    public void ShowEncyclopediaUnavailable()
    {
        if (_context is null) return;
        _noticeTween?.Kill();
        _encyclopediaNote.AddThemeColorOverride("font_color", _context.Tokens.ColorCaution);
        if (_context.ReducedMotion)
        {
            _noticeTween = CreateTween();
            _noticeTween.TweenInterval(1.5);
        }
        else
        {
            _noticeTween = CreateTween();
            _noticeTween.TweenProperty(_encyclopediaNote, "modulate:a", 0.6, 0.2);
            _noticeTween.TweenProperty(_encyclopediaNote, "modulate:a", 1.0, 0.2);
            _noticeTween.TweenInterval(1.1);
        }
        _noticeTween.TweenCallback(Callable.From(() => _encyclopediaNote.RemoveThemeColorOverride("font_color")));
    }

    private void ShowMet()
    {
        var met = _frozenMetMs();
        _met.Visible = met is not null;
        _frozenTag.Visible = met is not null;
        if (met is { } ms) _met.Text = "MET " + MetText.FromMilliseconds(ms);
    }

    private static string MissionLine(FrontendContext context)
    {
        if (context.Router.CurrentLaunch is not { } launch || context.Content(launch.MissionId) is not { } content) return string.Empty;
        var briefing = content.Briefing;
        return briefing.Vehicles.Count > 0 ? $"{briefing.Title} · {briefing.Vehicles[0].Name}" : briefing.Title;
    }
}
