using Godot;
using Usa.Game.Content;
using Usa.Game.Flow.Screens;
using Usa.Game.UI.Frontend.Model;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Briefing (<c>Briefing.tscn</c>, Figma G6-03; UI.md 5, GDD 3.3): premise, objectives with success criteria and weights
/// (R highlighted), crew cards, vehicles, known constraints and the budget note with its tape labels on the left and in the
/// middle; the approved NASA imagery carousel with rewritten captions and credits, the Historical context box (no program
/// names), the Guided first run toggle and Begin mission on the right. Never hints at the failure: it shows only briefing data.
/// </summary>
public sealed partial class BriefingScreen : Control, IFrontendScreen
{
    private Button _back = null!;
    private Label _title = null!;
    private Label _headerLine = null!;
    private Control _difficultyBadge = null!;
    private VBoxContainer _left = null!;
    private VBoxContainer _middle = null!;
    private Control _imageFrame = null!;
    private TextureRect _image = null!;
    private Label _imagePlaceholder = null!;
    private Button _previous = null!;
    private Button _next = null!;
    private Label _counter = null!;
    private Label _caption = null!;
    private Label _credit = null!;
    private VBoxContainer _history = null!;
    private Label _guidedLabel = null!;
    private Label _guidedHint = null!;
    private CheckButton _guided = null!;
    private Label _noPause = null!;
    private Button _begin = null!;
    private FrontendContext? _context;
    private BriefingModel? _model;
    private BackendStatusPanel? _status;
    private CarouselCursor _cursor;

    public override void _Ready()
    {
        _back = GetNode<Button>("%Back");
        _title = GetNode<Label>("%Title");
        _headerLine = GetNode<Label>("%HeaderLine");
        _difficultyBadge = GetNode<Control>("%DifficultyBadge");
        _left = GetNode<VBoxContainer>("%Left");
        _middle = GetNode<VBoxContainer>("%Middle");
        _imageFrame = GetNode<Control>("%ImageFrame");
        _image = GetNode<TextureRect>("%Image");
        _imagePlaceholder = GetNode<Label>("%ImagePlaceholder");
        _previous = GetNode<Button>("%Previous");
        _next = GetNode<Button>("%Next");
        _counter = GetNode<Label>("%Counter");
        _caption = GetNode<Label>("%Caption");
        _credit = GetNode<Label>("%Credit");
        _history = GetNode<VBoxContainer>("%History");
        _guidedLabel = GetNode<Label>("%GuidedLabel");
        _guidedHint = GetNode<Label>("%GuidedHint");
        _guided = GetNode<CheckButton>("%Guided");
        _noPause = GetNode<Label>("%NoPause");
        _begin = GetNode<Button>("%Begin");

        _back.Pressed += () => _model?.Back();
        _begin.Pressed += () => _model?.Start();
        _begin.SetMeta(Presentation.Cues.UiSoundCue.MetaKey, "confirm");
        _guided.Toggled += on => _model?.SetGuidedFirstRun(on);
        _previous.Pressed += () => ShowImage(_cursor.Previous());
        _next.Pressed += () => ShowImage(_cursor.Next());
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        if (context.Router.Selection is not { } selection
            || context.Content(selection.MissionId) is not { } content
            || context.Difficulty(selection.Difficulty) is not { } difficulty)
        {
            context.Log.Error("Frontend", "briefing opened without a loadable selection");
            return;
        }

        var text = context.Text;
        var tokens = context.Tokens;
        _model = new BriefingModel(context.Router, content, difficulty, context.Profiles.LoadProfile(),
            context.Services.Settings.Current.Gameplay, context.Manifest);
        var model = _model;

        _back.Text = text.Get("briefing.back");
        Ui.SetIcon(_back, Ui.IconBack, tokens.ColorTextMid);
        _title.Text = model.Title;
        _headerLine.Text = MenuText.BriefingHeaderLine(model.Header, text);
        Ui.Clear(_difficultyBadge);
        _difficultyBadge.AddChild(Ui.Badge(context.DifficultyLabel(model.DifficultyId)));

        BuildLeft(model, text, tokens);
        BuildMiddle(model, text, tokens);
        BuildHistory(model);

        _previous.Text = text.Get("briefing.previous");
        _next.Text = text.Get("briefing.next");
        _cursor = CarouselCursor.Start(model.Images.Count);
        _previous.Disabled = _next.Disabled = model.Images.Count < 2;
        ShowImage(_cursor);

        _guidedLabel.Text = model.GuidedFirstRunLabel;
        _guidedHint.Text = model.GuidedFirstRunHint;
        _guided.SetPressedNoSignal(model.GuidedFirstRun);
        _noPause.Text = text.Get("briefing.noPause");
        _begin.Text = model.StartLabel;

        // GDD 15.6: Begin waits for the ground link; the line above the footer says why and offers Retry.
        if (_begin.GetParent() is Control footer && footer.GetParent() is Control right)
        {
            _status = new BackendStatusPanel { Name = "BackendStatus" };
            right.AddChild(_status);
            right.MoveChild(_status, footer.GetIndex());
            _status.Bind(context);
            _status.Changed += UpdateBegin;
        }
        UpdateBegin();
        Callable.From(() => { if (IsInstanceValid(_begin) && _begin.IsInsideTree()) _begin.GrabFocus(); }).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (_status is not null) _status.Changed -= UpdateBegin;
    }

    private void UpdateBegin() => _begin.Disabled = !(_status?.MissionMayStart ?? true);

    private void BuildLeft(BriefingModel model, FrontendText text, UiTokenSet tokens)
    {
        Ui.Clear(_left);
        _left.AddChild(Ui.Label(text.Get("briefing.premiseTitle").ToUpperInvariant(), Ui.H2));
        foreach (var paragraph in model.Premise) _left.AddChild(Ui.Label(paragraph, "", wrap: true));

        _left.AddChild(Ui.Label(model.ObjectivesTitle.ToUpperInvariant(), Ui.H2));
        foreach (var objective in model.Objectives)
        {
            var isReturn = objective.Id.Value == DebriefPresentation.ReturnObjectiveId;
            var column = Ui.Column(8);
            var head = Ui.Row(8);
            head.AddChild(Ui.Label(DebriefPresentation.CodeOf(objective.Id.Value), Ui.Meta));
            var title = Ui.Label(objective.Title, "", wrap: true);
            head.AddChild(title);
            if (!isReturn)
            {
                var weight = MenuText.ObjectiveTag(objective);
                var dot = weight.IndexOf('·', StringComparison.Ordinal);
                head.AddChild(Ui.Label(dot > 0 ? weight[(dot + 1)..].Trim() : string.Empty, Ui.Meta));
            }
            column.AddChild(head);
            column.AddChild(Ui.Label(objective.Summary, Ui.Small, wrap: true));
            foreach (var criterion in objective.SuccessCriteria)
            {
                var row = Ui.Row(8);
                row.AddChild(Ui.Icon(Ui.IconCheck, tokens.ColorTextMid, 14));
                row.AddChild(Ui.Label(criterion, Ui.Small, wrap: true));
                column.AddChild(row);
            }
            var panel = Ui.Panel(column, 12);
            if (isReturn) panel.AddThemeStyleboxOverride("panel", Outline(tokens, tokens.ColorAccent));
            _left.AddChild(panel);
            if (isReturn) _left.AddChild(Ui.Label(objective.WeightNote, Ui.Meta, wrap: true));
        }
    }

    private void BuildMiddle(BriefingModel model, FrontendText text, UiTokenSet tokens)
    {
        Ui.Clear(_middle);
        _middle.AddChild(Ui.Label(model.CrewTitle.ToUpperInvariant(), Ui.H2));
        var crew = Ui.Row(12);
        foreach (var member in model.Crew)
        {
            var column = Ui.Column(8);
            var head = Ui.Row(12);
            var tag = Ui.Panel(Ui.Label(member.NameTag, Ui.Meta), 4, "Badge");
            tag.CustomMinimumSize = new Vector2(56, 56);
            head.AddChild(tag);
            var who = Ui.Column(4);
            who.AddChild(Ui.Label(member.Name.ToUpperInvariant(), Ui.H2));
            who.AddChild(Ui.Label(text.Format("briefing.crewAge", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["role"] = member.Role,
                ["age"] = member.Age.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }), Ui.Small));
            head.AddChild(who);
            column.AddChild(head);
            column.AddChild(Ui.Label(member.Bio, Ui.Small, wrap: true));
            var strengths = new HFlowContainer();
            strengths.AddThemeConstantOverride("h_separation", 6);
            strengths.AddThemeConstantOverride("v_separation", 6);
            foreach (var strength in member.Strengths) strengths.AddChild(Ui.Badge(strength));
            column.AddChild(strengths);
            var card = Ui.Panel(column, 12);
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            crew.AddChild(card);
        }
        _middle.AddChild(crew);

        _middle.AddChild(Ui.Label(model.VehicleTitle.ToUpperInvariant(), Ui.H2));
        var vehicles = Ui.Column(12);
        foreach (var vehicle in model.Vehicles)
        {
            var line = Ui.Row(8);
            line.AddChild(Ui.Label(vehicle.Name, Ui.Readout));
            line.AddChild(Ui.Label(vehicle.Summary, Ui.Small, wrap: true));
            vehicles.AddChild(line);
            if (vehicle.Sections.Count > 0) vehicles.AddChild(Ui.Label(string.Join(" · ", vehicle.Sections.Select(s => s.Name)), Ui.Meta, wrap: true));
        }
        _middle.AddChild(Ui.Panel(vehicles, 12));

        _middle.AddChild(Ui.Label(model.ConstraintsTitle.ToUpperInvariant(), Ui.H2));
        foreach (var constraint in model.KnownConstraints) _middle.AddChild(Ui.Label("–  " + constraint, Ui.Small, wrap: true));

        var note = model.BudgetNote;
        var budget = Ui.Column(8);
        var budgetHead = Ui.Row(8);
        budgetHead.AddChild(Ui.Label(note.Title.ToUpperInvariant(), Ui.H2));
        budgetHead.AddChild(Ui.Spacer(vertical: false));
        if (model.BudgetTapeLabels.Count > 0) budgetHead.AddChild(Ui.TapeLabel(model.BudgetTapeLabels[0]));
        budget.AddChild(budgetHead);
        budget.AddChild(Ui.Label(note.Text, "", wrap: true));
        var signed = Ui.Row(8);
        signed.AddChild(Ui.Label("— " + note.SignedBy, Ui.Small));
        signed.AddChild(Ui.Spacer(vertical: false));
        foreach (var label in model.BudgetTapeLabels.Skip(1)) signed.AddChild(Ui.TapeLabel(label));
        budget.AddChild(signed);
        _middle.AddChild(Ui.Panel(budget, 12));
    }

    private void BuildHistory(BriefingModel model)
    {
        Ui.Clear(_history);
        var context = model.HistoricalContext;
        var head = Ui.Row(8);
        head.AddChild(Ui.Label(context.Title.ToUpperInvariant(), Ui.H2));
        head.AddChild(Ui.Spacer(vertical: false));
        head.AddChild(Ui.Badge(context.Label));
        _history.AddChild(head);
        _history.AddChild(Ui.Label(context.Text, Ui.Small, wrap: true));
    }

    private void ShowImage(CarouselCursor cursor)
    {
        if (_model is not { } model || _context is not { } context) return;
        _cursor = cursor;
        var text = context.Text;
        _counter.Text = text.Format("briefing.imageCounter", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["index"] = cursor.OrdinalText,
            ["count"] = cursor.CountText,
        });
        if (cursor.IsEmpty)
        {
            _image.Texture = null;
            _imagePlaceholder.Visible = true;
            _imagePlaceholder.Text = text.Get("briefing.imageNotCached");
            _caption.Text = _credit.Text = string.Empty;
            return;
        }

        var image = model.Images[cursor.Index];
        var texture = Ui.LoadImage(image.LocalPath);
        _image.Texture = texture;
        _imagePlaceholder.Visible = texture is null;
        _imagePlaceholder.Text = image.AltText is { } alt
            ? text.Format("briefing.altText", new Dictionary<string, string>(StringComparer.Ordinal) { ["alt"] = alt })
            : text.Get("briefing.imageNotCached");
        _image.TooltipText = image.AltText ?? string.Empty;
        _caption.Text = image.Caption ?? string.Empty;
        _credit.Text = image.CreditLine is { } credit
            ? text.Format("briefing.credit", new Dictionary<string, string>(StringComparer.Ordinal) { ["credit"] = credit })
            : string.Empty;
        _imageFrame.TooltipText = image.AltText ?? string.Empty;
    }

    private static StyleBoxFlat Outline(UiTokenSet tokens, Color border) => new()
    {
        BgColor = tokens.ColorBg1,
        BorderColor = border,
        BorderWidthLeft = tokens.BorderWidthPx,
        BorderWidthTop = tokens.BorderWidthPx,
        BorderWidthRight = tokens.BorderWidthPx,
        BorderWidthBottom = tokens.BorderWidthPx,
        CornerRadiusTopLeft = tokens.RadiusPanelPx,
        CornerRadiusTopRight = tokens.RadiusPanelPx,
        CornerRadiusBottomLeft = tokens.RadiusPanelPx,
        CornerRadiusBottomRight = tokens.RadiusPanelPx,
    };
}
