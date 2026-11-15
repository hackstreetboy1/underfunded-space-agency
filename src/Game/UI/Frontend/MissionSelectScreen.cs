using Godot;
using Usa.Game.Content;
using Usa.Game.Flow.Data;
using Usa.Game.Flow.Screens;
using Usa.Game.Services.Saves;
using Usa.Game.UI.Frontend.Model;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Mission select (<c>MissionSelect.tscn</c>, Figma G6-02; UI.md 5): the difficulty segmented control (Easy, Normal; Hard
/// locked "with the campaign") and its condensed hint (decision 7), the Out of Control card with era, premise, the four
/// objectives with weights, best grade per difficulty (record grades in Grade S 40/48), the S+ ribbon and Open briefing,
/// then the locked campaign cards (era, title and Locked only).
/// </summary>
public sealed partial class MissionSelectScreen : Control, IFrontendScreen
{
    private Button _back = null!;
    private Label _title = null!;
    private Label _difficultyTitle = null!;
    private HBoxContainer _difficulties = null!;
    private Label _difficultyHint = null!;
    private HBoxContainer _cards = null!;
    private FrontendContext? _context;
    private MissionSelectModel? _model;
    private PlayerProfile _profile = new();
    private Button? _openBriefing;

    public override void _Ready()
    {
        _back = GetNode<Button>("%Back");
        _title = GetNode<Label>("%Title");
        _difficultyTitle = GetNode<Label>("%DifficultyTitle");
        _difficulties = GetNode<HBoxContainer>("%Difficulties");
        _difficultyHint = GetNode<Label>("%DifficultyHint");
        _cards = GetNode<HBoxContainer>("%Cards");
        _back.Pressed += () => _model?.Back();
    }

    public override void _ExitTree()
    {
        if (_model is not null) _model.Changed -= Render;
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        if (context.Catalog() is not { } catalog) return;
        _profile = context.Profiles.LoadProfile();
        _model = new MissionSelectModel(context.Router, catalog, _profile, context.Difficulties, context.Strings);
        _back.Text = context.Text.Get("mainMenu.back");
        Ui.SetIcon(_back, Ui.IconBack, context.Tokens.ColorTextMid);
        _title.Text = _model.Title;
        _difficultyTitle.Text = _model.DifficultyTitle.ToUpperInvariant();
        _model.Changed += Render;
        Render();
        Callable.From(() => { if (_openBriefing is not null && IsInstanceValid(_openBriefing) && _openBriefing.IsInsideTree()) _openBriefing.GrabFocus(); }).CallDeferred();
    }

    private void Render()
    {
        if (_model is not { } model || _context is not { } context) return;
        var text = context.Text;

        Ui.Clear(_difficulties);
        foreach (var option in model.Difficulties)
        {
            var id = option.Id;
            var selected = option.Id == model.SelectedDifficulty;
            var button = Ui.Button(option.Label, selected ? "" : Ui.ButtonGhost, () => _model?.SelectDifficulty(id));
            if (!option.Enabled) Ui.SetIcon(button, Ui.IconLocked, context.Tokens.ColorTextOff);
            button.ToggleMode = true;
            button.SetPressedNoSignal(selected);
            button.Disabled = !option.Enabled;
            button.TooltipText = option.LockedNote ?? string.Empty;
            button.CustomMinimumSize = new Vector2(96, 40);
            _difficulties.AddChild(button);
        }

        var hintKey = $"missionSelect.difficultyHint.{model.SelectedDifficulty}";
        var hard = model.Difficulties.FirstOrDefault(d => !d.Enabled)?.LockedNote;
        var hint = text.GetOr(hintKey, string.Empty);
        _difficultyHint.Text = hard is null ? hint : $"{hint} {hard}".Trim();

        Ui.Clear(_cards);
        _openBriefing = null;
        foreach (var card in model.Cards)
        {
            var panel = card.Locked ? LockedCard(card, context.Tokens) : MissionCard(card, model, context);
            panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            panel.SizeFlagsStretchRatio = card.Locked ? 1f : 2f;
            _cards.AddChild(panel);
        }
    }

    private PanelContainer MissionCard(MissionCard card, MissionSelectModel model, FrontendContext context)
    {
        var text = context.Text;
        var tokens = context.Tokens;
        var briefing = context.Content(card.MissionId)?.Briefing;
        var column = Ui.Column(16);

        var image = new Control { CustomMinimumSize = new Vector2(0, 280), ClipContents = true };
        if (briefing is not null && CardImage(briefing, context) is { } texture)
        {
            image.AddChild(Ui.Fill(new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            }));
        }
        if (card.SPlusRibbon)
        {
            var ribbon = Ui.Badge(text.Get("missionSelect.splusRibbon"), "Accent");
            ribbon.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
            ribbon.GrowHorizontal = GrowDirection.Begin;
            ribbon.Position = new Vector2(-16, 16);
            image.AddChild(ribbon);
        }
        column.AddChild(image);

        var body = Ui.Column(12);
        var orbit = briefing?.Header.Orbit ?? card.Setting ?? string.Empty;
        body.AddChild(Ui.Label(MenuText.CardEyebrow(1, card.Era, orbit, text), Ui.Meta));
        body.AddChild(Ui.Label(card.Title, Ui.Display));
        if (briefing is not null)
        {
            var crew = string.Join(", ", briefing.Crew.Select(c => c.Name));
            body.AddChild(Ui.Label($"{briefing.Header.Vehicles} · {crew}", Ui.Small, wrap: true));
            var premise = briefing.Premise.Count > 1 ? briefing.Premise[1] : briefing.Premise.Count == 1 ? briefing.Premise[0] : null;
            if (premise is not null) body.AddChild(Ui.Label(premise, "", wrap: true));

            body.AddChild(Ui.Label(text.Get("missionSelect.objectivesTitle").ToUpperInvariant(), Ui.Meta));
            var objectives = Ui.Row(16);
            foreach (var objective in briefing.Objectives)
            {
                var cell = Ui.Column(2);
                cell.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var tag = objective.Id.Value == DebriefPresentation.ReturnObjectiveId ? "R" : MenuText.ObjectiveTag(objective);
                cell.AddChild(Ui.Label(tag, Ui.Meta));
                cell.AddChild(Ui.Label(objective.Title, "", wrap: true));
                objectives.AddChild(cell);
            }
            body.AddChild(objectives);
        }

        body.AddChild(Ui.Label(model.BestGradeTitle.ToUpperInvariant(), Ui.Meta));
        var records = Ui.Row(16);
        var flightObjectives = briefing is null ? 0 : MenuText.FlightObjectiveCount(briefing.Objectives);
        foreach (var record in card.Records) records.AddChild(RecordPanel(card, record, flightObjectives, context));
        var hard = model.Difficulties.FirstOrDefault(d => d.Id == DifficultyInfo.Hard);
        if (hard is not null)
        {
            var locked = Ui.Column(8);
            locked.AddChild(Ui.IconLabel(Ui.IconLocked, context.Tokens.ColorTextLo, hard.Label.ToUpperInvariant(), Ui.Meta));
            locked.AddChild(Ui.Label("—", Ui.GradeSmall));
            locked.AddChild(Ui.Label(text.Get("missionSelect.hardArrives"), Ui.Meta, wrap: true));
            var lockedPanel = Ui.Panel(locked, 16);
            lockedPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            lockedPanel.Modulate = new Color(1, 1, 1, 0.6f);
            records.AddChild(lockedPanel);
        }
        body.AddChild(records);
        body.AddChild(Ui.Spacer());

        var footer = Ui.Row(12);
        var difficultyLabel = model.SelectedDifficulty is { } d ? context.DifficultyLabel(d) : string.Empty;
        footer.AddChild(Ui.Label(text.Format("missionSelect.flyingOn", new Dictionary<string, string>(StringComparer.Ordinal) { ["difficulty"] = difficultyLabel }), Ui.Meta));
        footer.AddChild(Ui.Spacer(vertical: false));
        var open = Ui.Button(text.Get("missionSelect.openBriefing"), Ui.ButtonPrimary, () => _model?.Confirm());
        open.Disabled = !model.CanConfirm;
        footer.AddChild(open);
        _openBriefing = open;
        body.AddChild(footer);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;

        var bodyMargin = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var side in new[] { "margin_left", "margin_right", "margin_bottom" }) bodyMargin.AddThemeConstantOverride(side, 24);
        bodyMargin.AddChild(body);
        column.AddChild(bodyMargin);

        var panel = new PanelContainer();
        var selected = card.MissionId == model.SelectedMissionId;
        if (selected) panel.AddThemeStyleboxOverride("panel", SelectedCard(tokens));
        panel.AddChild(column);
        return panel;
    }

    private PanelContainer RecordPanel(MissionCard card, MissionDifficultyRecord record, int flightObjectives, FrontendContext context)
    {
        var text = context.Text;
        var best = _profile.Find(card.MissionId, record.DifficultyId);
        var column = Ui.Column(8);
        var head = Ui.Row(8);
        head.AddChild(Ui.Label(record.DifficultyLabel.ToUpperInvariant(), Ui.Meta));
        head.AddChild(Ui.Spacer(vertical: false));
        if (record.SPlusAchieved) head.AddChild(Ui.Badge(text.Get("missionSelect.splusAchieved"), "Accent"));
        column.AddChild(head);

        var grade = Ui.Row(12);
        grade.AddChild(Ui.Label(record.BestGradeLabel ?? "—", Ui.GradeSmall));
        if (MenuText.RecordScore(best, text) is { } score)
        {
            var points = Ui.Label(score, Ui.Readout);
            points.VerticalAlignment = VerticalAlignment.Center;
            grade.AddChild(points);
        }
        column.AddChild(grade);
        column.AddChild(Ui.Label(
            record.BestGradeLabel is null ? record.BestGradeText : MenuText.RecordObjectives(record, flightObjectives, text), Ui.Meta, wrap: true));

        var panel = Ui.Panel(column, 16);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (record.SPlusAchieved) panel.AddThemeStyleboxOverride("panel", SelectedCard(context.Tokens));
        return panel;
    }

    private static PanelContainer LockedCard(MissionCard card, Theme.UiTokenSet tokens)
    {
        var column = Ui.Column(16);
        var image = new CenterContainer { CustomMinimumSize = new Vector2(0, 280) };
        image.AddChild(Ui.Icon(Ui.IconLocked, tokens.ColorTextLo, 32));
        column.AddChild(image);
        var body = Ui.Column(12);
        body.AddChild(Ui.Label(card.Era, Ui.Meta));
        var title = Ui.Label(card.Title, Ui.Display, wrap: true);
        title.Modulate = new Color(1, 1, 1, 0.75f);
        body.AddChild(title);
        if (card.LockedLabel is { } locked) body.AddChild(Ui.Badge(locked));
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_bottom" }) margin.AddThemeConstantOverride(side, 24);
        margin.AddChild(body);
        column.AddChild(margin);
        var panel = new PanelContainer();
        panel.AddChild(column);
        return panel;
    }

    private static Texture2D? CardImage(BriefingDefinition briefing, FrontendContext context)
    {
        foreach (var slot in briefing.ImageSlots.OrderBy(s => s.Id == "img_docking" ? 0 : 1))
        {
            var item = context.Manifest.ForTouchpoint(NasaTouchpoints.Briefing)
                .FirstOrDefault(i => string.Equals(i.ItemId, slot.NasaId, StringComparison.Ordinal));
            if (Ui.LoadImage(item?.LocalPath) is { } texture) return texture;
        }
        return null;
    }

    private static StyleBoxFlat SelectedCard(Theme.UiTokenSet tokens) => new()
    {
        BgColor = tokens.ColorBg1,
        BorderColor = tokens.ColorAccent,
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = tokens.RadiusPanelPx,
        CornerRadiusTopRight = tokens.RadiusPanelPx,
        CornerRadiusBottomLeft = tokens.RadiusPanelPx,
        CornerRadiusBottomRight = tokens.RadiusPanelPx,
    };
}
