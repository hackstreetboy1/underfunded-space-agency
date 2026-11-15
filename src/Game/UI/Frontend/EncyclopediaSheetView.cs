using Godot;
using Usa.Game.Content;
using Usa.Game.Flow.Screens;
using Usa.Game.UI.Frontend.Model;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The encyclopedia (<c>EncyclopediaSheet.tscn</c>, Figma G6-07; UI.md 5): search, an "All" chip plus the data categories,
/// the entry list, the entry (first-use line, curated image or diagram slot, body, related links, sources, modern-reference
/// tag) and the "Ask the engineers" box, which explains and never recommends. In a mission it is the 760-wide side sheet
/// anchored left of the centre column (decision 6) while the simulation runs; from the main menu it fills a page and the
/// ask box is hidden, because the engineers answer through a running mission's advisor.
/// </summary>
public sealed partial class EncyclopediaSheetView : PanelContainer
{
    private Label _title = null!;
    private Control _simRunning = null!;
    private Label _closeHint = null!;
    private Button _close = null!;
    private LineEdit _search = null!;
    private HFlowContainer _chips = null!;
    private Label _listTitle = null!;
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private PanelContainer _askPanel = null!;
    private Label _askTitle = null!;
    private Label _askHint = null!;
    private Label _question = null!;
    private Label _answer = null!;
    private Label _answerSources = null!;
    private LineEdit _askInput = null!;
    private Button _askButton = null!;
    private Label _askDisclaimer = null!;
    private FrontendContext? _context;
    private EncyclopediaSheetModel? _model;
    private Action? _onClose;
    private bool _inMission;
    private (string, string?, string?, int)? _rendered;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _simRunning = GetNode<Control>("%SimRunning");
        _closeHint = GetNode<Label>("%CloseHint");
        _close = GetNode<Button>("%Close");
        _search = GetNode<LineEdit>("%Search");
        _chips = GetNode<HFlowContainer>("%Chips");
        _listTitle = GetNode<Label>("%ListTitle");
        _list = GetNode<VBoxContainer>("%List");
        _detail = GetNode<VBoxContainer>("%Detail");
        _askPanel = GetNode<PanelContainer>("%AskPanel");
        _askTitle = GetNode<Label>("%AskTitle");
        _askHint = GetNode<Label>("%AskHint");
        _question = GetNode<Label>("%Question");
        _answer = GetNode<Label>("%Answer");
        _answerSources = GetNode<Label>("%AnswerSources");
        _askInput = GetNode<LineEdit>("%AskInput");
        _askButton = GetNode<Button>("%AskButton");
        _askDisclaimer = GetNode<Label>("%AskDisclaimer");

        _close.Pressed += () => _onClose?.Invoke();
        _search.TextChanged += text => _model?.SetQuery(text);
        _askInput.TextChanged += text => _model?.SetQuestion(text);
        _askInput.TextSubmitted += _ => _model?.SubmitQuestion();
        _askButton.Pressed += () => _model?.SubmitQuestion();
    }

    public override void _ExitTree()
    {
        if (_model is not null) _model.Changed -= Render;
    }

    /// <param name="onClose">The ✕ button; null hides it (the full-screen page has its own back link).</param>
    /// <param name="inMission">The side sheet over the running console: shows SIM RUNNING, the close hint and the ask box.</param>
    public void Initialize(FrontendContext context, EncyclopediaSheetModel model, Action? onClose, bool inMission = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if (_model is not null) _model.Changed -= Render;
        _context = context;
        _model = model;
        _onClose = onClose;
        _inMission = inMission;
        _rendered = null;
        var text = context.Text;

        _title.Text = model.Title.ToUpperInvariant();
        Ui.Clear(_simRunning);
        _simRunning.AddChild(Ui.Badge(text.Get("encyclopedia.simRunning"), "Ok"));
        _simRunning.Visible = inMission;
        _closeHint.Text = text.Get("encyclopedia.closeHint");
        _closeHint.Visible = inMission;
        _close.Visible = onClose is not null;
        Ui.SetIcon(_close, Ui.IconClose, context.Tokens.ColorTextMid);
        _search.PlaceholderText = model.SearchLabel;
        _askPanel.Visible = inMission;
        _askTitle.Text = model.AskTitle.ToUpperInvariant();
        _askHint.Text = text.Get("encyclopedia.askHint");
        _askInput.PlaceholderText = model.AskPlaceholder;
        _askButton.Text = text.Get("encyclopedia.ask");
        _askDisclaimer.Text = model.AskDisclaimer;
        if (inMission) PlaceAsSheet(context);

        BuildChips();
        model.Changed += Render;
        if (model.Selected is null && model.Results.Count > 0) model.Select(model.Results[0].Id);
        else Render();
    }

    /// <summary>Focuses the search field when the sheet opens.</summary>
    public void Open() => Callable.From(() => _search.GrabFocus()).CallDeferred();

    // Decision 6: left edge of the centre column, below the resource strip, 760 wide; compact layout moves it left with the column.
    private void PlaceAsSheet(FrontendContext context)
    {
        var tokens = context.Tokens;
        var viewport = GetViewportRect().Size;
        var fullWidth = tokens.LayoutColLeftPx + tokens.LayoutColCentrePx + tokens.LayoutColRightPx + 4 * tokens.LayoutGutterPx;
        var leftColumn = viewport.X >= fullWidth ? tokens.LayoutColLeftPx : 352;
        var left = tokens.LayoutGutterPx * 2 + leftColumn;
        var top = tokens.LayoutHeaderPx + tokens.LayoutResourceStripPx + tokens.LayoutGutterPx;
        AnchorLeft = 0;
        AnchorRight = 0;
        AnchorTop = 0;
        AnchorBottom = 1;
        OffsetLeft = left;
        OffsetRight = left + EncyclopediaSheetModel.SheetWidthPx;
        OffsetTop = top;
        OffsetBottom = -tokens.LayoutGutterPx;
    }

    private void BuildChips()
    {
        if (_model is null) return;
        Ui.Clear(_chips);
        foreach (var option in _model.Categories)
        {
            var id = option.Id;
            var chip = Ui.Button(option.Label, Ui.ButtonGhost, () => _model?.SetCategory(id));
            chip.ToggleMode = true;
            chip.SetMeta("category", id ?? string.Empty);
            _chips.AddChild(chip);
        }
    }

    private void Render()
    {
        if (_model is not { } model || _context is not { } context) return;
        var text = context.Text;
        RenderAsk(model, text);

        // Typing a question changes the model too; the list and entry rebuild only when what they show changed.
        var key = (model.Query, model.Category, model.Selected?.Entry.Id, model.Results.Count);
        if (key == _rendered) return;
        _rendered = key;

        foreach (var child in _chips.GetChildren())
        {
            if (child is not Button chip) continue;
            var id = chip.GetMeta("category").AsString();
            chip.SetPressedNoSignal(string.Equals(id, model.Category ?? string.Empty, StringComparison.Ordinal));
            chip.ThemeTypeVariation = chip.ButtonPressed ? "" : Ui.ButtonGhost;
        }

        var categoryLabel = model.Categories.FirstOrDefault(c => c.Id == model.Category)?.Label ?? string.Empty;
        _listTitle.Text = MenuText.EncyclopediaListTitle(categoryLabel, model.Results.Count, text);
        Ui.Clear(_list);
        if (model.Results.Count == 0) _list.AddChild(Ui.Label(text.Get("encyclopedia.empty"), Ui.Meta, wrap: true));
        foreach (var entry in model.Results)
        {
            var id = entry.Id;
            var selected = model.Selected?.Entry.Id == id;
            var item = Ui.Button(entry.Title, selected ? "" : Ui.ButtonGhost, () => _model?.Select(id));
            item.Alignment = HorizontalAlignment.Left;
            item.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            item.TooltipText = context.Encyclopedia?.EraLabel(entry.Era) ?? entry.Era;
            _list.AddChild(item);
        }

        RenderDetail(model, context);
    }

    private void RenderDetail(EncyclopediaSheetModel model, FrontendContext context)
    {
        Ui.Clear(_detail);
        var text = context.Text;
        if (model.Selected is not { } view)
        {
            _detail.AddChild(Ui.Label(text.Get("encyclopedia.pickEntry"), Ui.Meta, wrap: true));
            return;
        }

        var entry = view.Entry;
        var head = Ui.Row(12);
        var title = Ui.Label(entry.Title, Ui.H1, wrap: true);
        head.AddChild(title);
        if (view.ModernReferenceTag is { } tag) head.AddChild(Ui.Badge(tag, "Info"));
        _detail.AddChild(head);
        _detail.AddChild(Ui.Label(entry.FirstUse, "", wrap: true));
        _detail.AddChild(ImageSlot(entry, context));
        _detail.AddChild(Ui.Label(entry.Body, "", wrap: true));

        if (view.Related.Count > 0)
        {
            var related = new HFlowContainer();
            related.AddThemeConstantOverride("h_separation", 8);
            related.AddChild(Ui.Label(text.Get("encyclopedia.related"), Ui.Meta));
            foreach (var other in view.Related)
            {
                var id = other.Id;
                related.AddChild(Ui.Button(other.Title, Ui.ButtonGhost, () => _model?.Select(id)));
            }
            _detail.AddChild(related);
        }

        if (view.SourceLines.Count > 0)
            _detail.AddChild(Ui.Label($"{model.SourcesLabel}: {string.Join(" · ", view.SourceLines)}", Ui.Meta, wrap: true));
    }

    private static Control ImageSlot(EncyclopediaEntry entry, FrontendContext context)
    {
        if (EncyclopediaImages.For(entry, context.Manifest) is { } image && Ui.LoadImage(image.LocalPath) is { } texture)
        {
            var column = Ui.Column(4);
            var size = texture.GetSize();
            var crop = (float)(size.Y * image.CropTopFraction);
            var region = new AtlasTexture { Atlas = texture, Region = new Rect2(0, crop, size.X, size.Y - crop) };
            column.AddChild(new TextureRect
            {
                Texture = region,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(0, 220),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            if (image.Caption is { } caption) column.AddChild(Ui.Label(caption, Ui.Small, wrap: true));
            column.AddChild(Ui.Label(image.Credit, Ui.Meta));
            return column;
        }
        return Ui.Panel(Ui.Label(context.Text.Get("encyclopedia.noImage"), Ui.Meta, wrap: true), 16);
    }

    private void RenderAsk(EncyclopediaSheetModel model, FrontendText text)
    {
        if (!_inMission) return;
        var state = model.QuestionState;
        _askButton.Disabled = state == EngineerQuestionState.Waiting || model.Question.Trim().Length == 0;
        _question.Visible = state != EngineerQuestionState.Idle;
        _question.Text = model.Question.Trim();
        _answerSources.Visible = state == EngineerQuestionState.Answered && model.AnswerSources.Count > 0;
        _answerSources.Text = $"{model.SourcesLabel}: {string.Join(" · ", model.AnswerSources)}";
        _answer.Visible = state != EngineerQuestionState.Idle;
        _answer.Text = state switch
        {
            EngineerQuestionState.Waiting => text.Get("encyclopedia.askWaiting"),
            EngineerQuestionState.Failed => text.Get("encyclopedia.askFailed"),
            _ => model.Answer ?? string.Empty,
        };
        if (_askInput.Text != model.Question) _askInput.Text = model.Question;
    }
}
