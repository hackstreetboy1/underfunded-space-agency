using Godot;
using Usa.Game.Flow.Screens;
using Usa.Game.UI.Frontend.Model;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Main menu (<c>MainMenu.tscn</c>, Figma G6-01; UI.md 5): full-bleed backdrop rotating per launch (the approved EPIC Earth
/// stands in for APOD) under the left legibility gradient (<c>legibility/*</c> tokens), the title lockup with the tape mark and
/// tape subtitle, Continue (with its resume line) / Missions / Encyclopedia / Settings / Credits / Quit, and the backdrop's
/// credit chip with its MODERN REFERENCE tag. Under the menu, the ground-link line (<see cref="BackendStatusPanel"/>); Continue
/// stays disabled while the mission may not start (GDD 15.6).
/// </summary>
public sealed partial class MainMenuScreen : Control, IFrontendScreen
{
    private TextureRect _backdrop = null!;
    private TextureRect _legibility = null!;
    private Label _mark = null!;
    private Label _agency = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private VBoxContainer _menu = null!;
    private Label _notice = null!;
    private Label _keysHint = null!;
    private PanelContainer _creditChip = null!;
    private HBoxContainer _creditRow = null!;
    private FrontendContext? _context;
    private MainMenuModel? _model;
    private BackendStatusPanel? _status;
    private Button? _continue;

    public override void _Ready()
    {
        _backdrop = GetNode<TextureRect>("%Backdrop");
        _legibility = GetNode<TextureRect>("%Legibility");
        _mark = GetNode<Label>("%Mark");
        _agency = GetNode<Label>("%Agency");
        _title = GetNode<Label>("%Title");
        _subtitle = GetNode<Label>("%Subtitle");
        _menu = GetNode<VBoxContainer>("%Menu");
        _notice = GetNode<Label>("%Notice");
        _keysHint = GetNode<Label>("%KeysHint");
        _creditChip = GetNode<PanelContainer>("%CreditChip");
        _creditRow = GetNode<HBoxContainer>("%CreditRow");
    }

    public override void _ExitTree()
    {
        if (_model is not null) _model.Changed -= BuildMenu;
        if (_status is not null) _status.Changed -= UpdateContinue;
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        var text = context.Text;
        var tokens = context.Tokens;
        _model = new MainMenuModel(context.Router, context.Profiles, context.Strings, context.Manifest, context.Services.LaunchCount);

        _mark.Text = text.Get("agency.headerMark");
        _agency.Text = text.Get("agency.officialName");
        _title.Text = _model.Title;
        _subtitle.Text = _model.Subtitle;
        _keysHint.Text = text.Get("mainMenu.keysHint");

        _legibility.Texture = new GradientTexture2D
        {
            Gradient = new Gradient
            {
                Offsets = [0f, 0.45f, 1f],
                Colors = [tokens.ColorLegibilityStrong, tokens.ColorLegibilitySoft, tokens.ColorLegibilityClear],
            },
            FillFrom = new Vector2(0, 0),
            FillTo = new Vector2(1, 0),
            Width = 256,
            Height = 4,
        };

        var backdrop = _model.Backdrop;
        _backdrop.Texture = Ui.LoadImage(backdrop?.LocalPath);
        _creditChip.Visible = backdrop is not null;
        Ui.Clear(_creditRow);
        if (backdrop is not null)
        {
            _creditRow.AddChild(Ui.Label(backdrop.Label, Ui.Small));
            if (backdrop.ModernReferenceTag is { } tag) _creditRow.AddChild(Ui.Badge(tag, "Info"));
            _creditChip.TooltipText = backdrop.Caption ?? string.Empty;
        }

        _notice.Visible = context.Router.LastAborted is not null;
        _notice.Text = text.Get("mainMenu.abortNotice");

        _status = new BackendStatusPanel { Name = "BackendStatus", CustomMinimumSize = new Vector2(320, 0) };
        _status.Bind(context);
        _status.Changed += UpdateContinue;

        _model.Changed += BuildMenu;
        BuildMenu();
    }

    private void UpdateContinue()
    {
        if (_continue is not null && IsInstanceValid(_continue) && _model is { } model)
            _continue.Disabled = !model.ContinueEnabled || !(_status?.MissionMayStart ?? true);
    }

    private void BuildMenu()
    {
        if (_model is not { } model || _context is not { } context) return;
        // The status line survives the rebuild (it keeps its binding); only the buttons are rebuilt.
        if (_status is not null && _status.GetParent() == _menu) _menu.RemoveChild(_status);
        Ui.Clear(_menu);
        _continue = null;
        Button? first = null;
        foreach (var item in model.Buttons)
        {
            if (item.Action == MainMenuAction.Continue && !item.Enabled) continue;
            if (item.Action == MainMenuAction.Quit) _menu.AddChild(new Control { CustomMinimumSize = new Vector2(0, 32), MouseFilter = MouseFilterEnum.Ignore });

            var action = item.Action;
            var variation = action switch
            {
                MainMenuAction.Continue => Ui.ButtonPrimary,
                MainMenuAction.Quit => Ui.ButtonGhost,
                _ => "",
            };
            var button = Ui.Button(item.Label, variation, () => _model?.Activate(action));
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new Vector2(320, 44);
            button.Disabled = !item.Enabled;
            _menu.AddChild(button);
            first ??= button;
            if (action == MainMenuAction.Continue) _continue = button;

            if (action == MainMenuAction.Continue && model.ContinueTarget is { } resume)
            {
                var title = context.Content(resume.MissionId)?.Briefing.Title ?? resume.MissionId;
                _menu.AddChild(Ui.Label(MenuText.ContinueLine(resume, title, context.DifficultyLabel(resume.Difficulty), context.Text), Ui.Meta));
            }
        }
        if (_status is not null)
        {
            _menu.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16), MouseFilter = MouseFilterEnum.Ignore });
            _menu.AddChild(_status);
        }
        UpdateContinue();
        Callable.From(() => { if (first is not null && IsInstanceValid(first) && first.IsInsideTree()) first.GrabFocus(); }).CallDeferred();
    }
}
