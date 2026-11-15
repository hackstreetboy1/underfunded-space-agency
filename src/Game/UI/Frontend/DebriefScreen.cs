using Godot;
using Usa.Game.Content;
using Usa.Game.Flow.Data;
using Usa.Game.Flow.Profile;
using Usa.Game.Flow.Screens;
using Usa.Game.UI.Frontend.Model;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Debrief (<c>Debrief.tscn</c>, Figma G6-04a/b; UI.md 5, GDD 10.4): grade in Grade 120/128 with the band line, final and raw
/// score, the cap note, each crew loss with its MET, the splashdown line and whether the run was saved
/// (<c>ProfileRecorder.StatusOf</c>); five dimension bars (tone from the ratio, points always shown); objectives with state, why, MET
/// and cause, plus the R row; crew condition; the timeline from <c>DebriefFacts</c> with "what you knew then"; best
/// decisions and the largest avoidable loss; the S+ box; the real-history card with its "Jump to" row (decision 8) and all
/// three provenance labels; Replay (same seed), Retry with a new variant and Mission select, the first two waiting for the
/// ground link (<see cref="BackendStatusPanel"/>).
/// </summary>
public sealed partial class DebriefScreen : Control, IFrontendScreen
{
    private Label _title = null!;
    private Label _meta = null!;
    private Button _replay = null!;
    private Button _retry = null!;
    private Button _missionSelect = null!;
    private VBoxContainer _content = null!;
    private ScrollContainer _historyScroll = null!;
    private readonly Dictionary<string, Control> _historyAnchors = new(StringComparer.Ordinal);
    private DebriefModel? _model;
    private BackendStatusPanel? _status;

    public override void _ExitTree()
    {
        if (_status is not null) _status.Changed -= UpdateLaunchButtons;
    }

    private void UpdateLaunchButtons()
    {
        var mayStart = _status?.MissionMayStart ?? true;
        _replay.Disabled = !mayStart;
        _retry.Disabled = !mayStart;
    }

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _meta = GetNode<Label>("%Meta");
        _replay = GetNode<Button>("%Replay");
        _retry = GetNode<Button>("%Retry");
        _missionSelect = GetNode<Button>("%MissionSelect");
        _content = GetNode<VBoxContainer>("%Content");
        _replay.Pressed += () => _model?.Replay();
        _retry.Pressed += () => _model?.Retry();
        _missionSelect.Pressed += () => _model?.MissionSelect();
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Router.LastCompleted is not { } completed)
        {
            context.Log.Error("Frontend", "debrief opened without a completed mission");
            return;
        }

        // The host recorded the run when the mission ended; the screen only shows what that did (never recorded reads as not saved).
        var record = context.Services.Profile.StatusOf(completed.Launch.RunNumber) ?? ProfileRecordStatus.Failed;
        _model = new DebriefModel(context.Router, completed, record);
        var briefing = context.Content(_model.MissionId)?.Briefing;
        var view = _model.View;
        var sheet = DebriefPresentation.Build(view, briefing?.Title ?? _model.MissionId, context.DifficultyLabel(_model.Difficulty),
            _model.Seed, briefing?.Objectives ?? [], context.Bands, context.Text, saved: _model.Saved);

        // Replay and Retry start a mission, so they wait for the ground link like Begin (GDD 15.6).
        if (_title.GetParent() is Control titles)
        {
            _status = new BackendStatusPanel { Name = "BackendStatus" };
            titles.AddChild(_status);
            _status.Bind(context);
            _status.Changed += UpdateLaunchButtons;
        }
        UpdateLaunchButtons();

        _title.Text = sheet.Title;
        _meta.Text = sheet.Meta;
        _replay.Text = _model.ReplayLabel;
        _retry.Text = _model.RetryLabel;
        _retry.Visible = context.Difficulty(_model.Difficulty)?.RetryNewVariant ?? true;
        _missionSelect.Text = _model.MissionSelectLabel;

        Ui.Clear(_content);
        var tokens = context.Tokens;
        var top = Ui.Row(24);
        top.AddChild(Sized(GradePanel(sheet, tokens), 1f));
        top.AddChild(Sized(DimensionsPanel(sheet, context.Text, tokens), 2f));
        _content.AddChild(top);

        var columns = Ui.Row(24);
        columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        var left = Ui.Column(24);
        left.AddChild(ObjectivesPanel(sheet, context.Text, tokens));
        left.AddChild(CrewPanel(sheet, view.Labels));
        var middle = Ui.Column(24);
        middle.AddChild(TimelinePanel(sheet, view.Labels, tokens));
        middle.AddChild(DecisionsPanel(view.Labels.BestDecisions, sheet.BestDecisions, tokens, critical: false));
        if (sheet.LargestAvoidableLoss is { } loss) middle.AddChild(DecisionsPanel(view.Labels.LargestAvoidableLoss, [loss], tokens, critical: true));
        var right = Ui.Column(24);
        right.AddChild(SPlusPanel(sheet, tokens));
        right.AddChild(HistoryPanel(view, briefing, context));
        columns.AddChild(Sized(left, 1f));
        columns.AddChild(Sized(middle, 1f));
        columns.AddChild(Sized(right, 1f));
        _content.AddChild(columns);

        _missionSelect.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static PanelContainer GradePanel(DebriefPresentation sheet, UiTokenSet tokens)
    {
        var row = Ui.Row(24);
        var grade = Ui.Label(sheet.GradeLabel, Ui.Grade);
        grade.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(grade);
        var column = Ui.Column(8);
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.Alignment = BoxContainer.AlignmentMode.Center;
        column.AddChild(Ui.Label(sheet.BandLine, Ui.Small));
        var final = Ui.Row(8);
        final.AddChild(Ui.Label(sheet.FinalText, "ReadoutLarge"));
        var suffix = Ui.Label(sheet.FinalSuffix, Ui.Meta);
        suffix.VerticalAlignment = VerticalAlignment.Bottom;
        final.AddChild(suffix);
        column.AddChild(final);
        column.AddChild(Ui.Label(sheet.RawLine, Ui.ReadoutSmall));
        if (sheet.CapNote is { } cap)
        {
            var note = Ui.Row(8);
            note.AddChild(StatusGlyph.Create(DebriefTone.Critical, tokens.ColorCritical));
            note.AddChild(Ui.Label(cap, Ui.Small, wrap: true));
            var box = Ui.Panel(note, 8);
            box.AddThemeStyleboxOverride("panel", Tinted(tokens.ColorTintCritical, tokens.ColorCritical, tokens));
            column.AddChild(box);
        }
        // Crew losses stay quiet (UI.md 3.4: text.mid, no red), then where the capsule came down.
        foreach (var line in sheet.FlightLines) column.AddChild(Ui.Label(line, Ui.Small, wrap: true));
        if (sheet.RecordLine is { } record)
        {
            var saved = Ui.Row(8);
            var tone = sheet.RecordSaved ? DebriefTone.Ok : DebriefTone.Caution;
            saved.AddChild(StatusGlyph.Create(tone, Ui.ToneColour(tone, tokens)));
            saved.AddChild(Ui.Label(record, Ui.Meta, wrap: true));
            column.AddChild(saved);
        }
        row.AddChild(column);
        return Ui.Panel(row, 24);
    }

    private static PanelContainer DimensionsPanel(DebriefPresentation sheet, FrontendText text, UiTokenSet tokens)
    {
        var column = Ui.Column(12);
        column.AddChild(Ui.Label(text.Get("debrief.dimensionsTitle").ToUpperInvariant(), Ui.H2));
        foreach (var dimension in sheet.Dimensions)
        {
            var row = Ui.Row(24);
            var name = Ui.Label(dimension.Name);
            name.CustomMinimumSize = new Vector2(180, 0);
            row.AddChild(name);
            row.AddChild(Ui.Bar(dimension.Ratio, Ui.ToneColour(dimension.Tone, tokens), tokens, 8));
            var points = Ui.Label($"{dimension.PointsText} / {dimension.MaxText}", Ui.Readout);
            points.CustomMinimumSize = new Vector2(88, 0);
            points.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(points);
            column.AddChild(row);
        }
        return Ui.Panel(column, 24);
    }

    private static PanelContainer ObjectivesPanel(DebriefPresentation sheet, FrontendText text, UiTokenSet tokens)
    {
        var column = Ui.Column(12);
        column.AddChild(Ui.Label(text.Get("debrief.objectivesTitle").ToUpperInvariant(), Ui.H2));
        foreach (var objective in sheet.Objectives)
        {
            var block = Ui.Column(4);
            var head = Ui.Row(8);
            head.AddChild(StatusGlyph.Create(objective.Tone, Ui.ToneColour(objective.Tone, tokens)));
            head.AddChild(Ui.Label(objective.Code, Ui.Meta));
            var title = Ui.Label(objective.Title, "", wrap: true);
            head.AddChild(title);
            var state = Ui.Label(objective.StateText, Ui.Small);
            if (objective.Tone == DebriefTone.Ok) state.AddThemeColorOverride("font_color", tokens.ColorOk);
            head.AddChild(state);
            block.AddChild(head);
            if (objective.Detail.Length > 0) block.AddChild(Ui.Label(objective.Detail, Ui.Small, wrap: true));
            if (objective.LossNote is { } loss)
            {
                var line = Ui.Row(8);
                if (objective.MetText is { } met) line.AddChild(Ui.Label(met, Ui.ReadoutSmall));
                var note = Ui.Label(loss, Ui.Small, wrap: true);
                if (objective.Tone == DebriefTone.Critical) note.AddThemeColorOverride("font_color", tokens.ColorCaution);
                line.AddChild(note);
                block.AddChild(line);
            }
            column.AddChild(block);
        }
        return Ui.Panel(column, 16);
    }

    private static PanelContainer CrewPanel(DebriefPresentation sheet, DebriefLabels labels)
    {
        var column = Ui.Column(12);
        column.AddChild(Ui.Label(labels.CrewCondition.ToUpperInvariant(), Ui.H2));
        foreach (var member in sheet.Crew)
        {
            var row = Ui.Row(16);
            var name = Ui.Label(member.Name);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(Cell(labels.CrewLaunch, member.LaunchText));
            row.AddChild(Cell(labels.CrewWorst, member.WorstText));
            row.AddChild(Cell(labels.CrewRecovery, member.RecoveryText));
            column.AddChild(row);
        }
        return Ui.Panel(column, 16);

        static Control Cell(string label, string value)
        {
            var cell = Ui.Column(2);
            cell.CustomMinimumSize = new Vector2(96, 0);
            cell.AddChild(Ui.Label(label, Ui.Meta));
            cell.AddChild(Ui.Label(value, Ui.ReadoutSmall));
            return cell;
        }
    }

    private static PanelContainer TimelinePanel(DebriefPresentation sheet, DebriefLabels labels, UiTokenSet tokens)
    {
        var column = Ui.Column(12);
        column.AddChild(Ui.Label(labels.Timeline.ToUpperInvariant(), Ui.H2));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 420), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var rows = Ui.Column(8);
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (var entry in sheet.Timeline)
        {
            var row = Ui.Row(12);
            var met = Ui.Label(entry.MetText, Ui.ReadoutSmall);
            met.CustomMinimumSize = new Vector2(72, 0);
            met.VerticalAlignment = VerticalAlignment.Top;
            row.AddChild(met);
            var tone = entry.Kind == DebriefTimelineKind.Incident ? DebriefTone.Critical : DebriefTone.Neutral;
            row.AddChild(StatusGlyph.Create(tone, Ui.ToneColour(tone, tokens)));
            var body = Ui.Column(2);
            body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            body.AddChild(Ui.Label($"{entry.KindText}: {entry.Text}", "", wrap: true));
            if (entry.KnownLine is { } known) body.AddChild(Ui.Label(known, Ui.Small, wrap: true));
            row.AddChild(body);
            rows.AddChild(row);
            rows.AddChild(Ui.Divider(tokens));
        }
        scroll.AddChild(rows);
        column.AddChild(scroll);
        return Ui.Panel(column, 16);
    }

    private static PanelContainer DecisionsPanel(string title, IReadOnlyList<DebriefDecisionRow> decisions, UiTokenSet tokens, bool critical)
    {
        var column = Ui.Column(8);
        column.AddChild(Ui.Label(title.ToUpperInvariant(), Ui.H2));
        foreach (var decision in decisions)
        {
            var row = Ui.Row(8);
            if (critical) row.AddChild(StatusGlyph.Create(DebriefTone.Caution, tokens.ColorCaution));
            else row.AddChild(Ui.Icon(Ui.IconCheck, tokens.ColorOk, 14));
            var text = decision.MetText is { } met ? $"{decision.Text} ({met})" : decision.Text;
            row.AddChild(Ui.Label(text, Ui.Small, wrap: true));
            column.AddChild(row);
        }
        var panel = Ui.Panel(column, 16);
        if (critical) panel.AddThemeStyleboxOverride("panel", Tinted(tokens.ColorBg1, tokens.ColorCaution, tokens));
        return panel;
    }

    private static PanelContainer SPlusPanel(DebriefPresentation sheet, UiTokenSet tokens)
    {
        var column = Ui.Column(8);
        column.AddChild(Ui.Label(sheet.SPlusTitle.ToUpperInvariant(), Ui.H2));
        foreach (var reason in sheet.SPlusReasons)
        {
            var row = Ui.Row(8);
            var tone = sheet.SPlusAchieved ? DebriefTone.Ok : DebriefTone.Neutral;
            row.AddChild(StatusGlyph.Create(tone, Ui.ToneColour(tone, tokens)));
            row.AddChild(Ui.Label(reason, "", wrap: true));
            column.AddChild(row);
        }
        return Ui.Panel(column, 16);
    }

    private PanelContainer HistoryPanel(DebriefView view, BriefingDefinition? briefing, FrontendContext context)
    {
        var text = context.Text;
        var labels = view.Labels;
        var card = view.HistoryCard;
        _historyAnchors.Clear();

        var column = Ui.Column(12);
        column.AddChild(Ui.Label(text.Get("debrief.realHistoryTitle").ToUpperInvariant(), Ui.H2));
        var jump = Ui.Row(8);
        jump.AddChild(Ui.Label(text.Get("debrief.jumpTo"), Ui.Meta));
        foreach (var label in new[] { labels.DocumentedHistory, labels.SimulationResult, labels.FictionalAdaptation })
            jump.AddChild(Ui.Button(label.ToUpperInvariant(), Ui.ButtonGhost, () => JumpTo(label)));
        column.AddChild(jump);

        _historyScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 460), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var body = Ui.Column(12);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var image = Ui.Column(4);
        if (HistoryImage(card, briefing, context) is { } texture)
        {
            image.AddChild(new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(0, 200),
                TooltipText = card.ImageAltText ?? string.Empty,
            });
        }
        else if (card.ImageAltText is { } alt)
        {
            image.AddChild(Ui.Label(text.Format("briefing.altText", new Dictionary<string, string>(StringComparer.Ordinal) { ["alt"] = alt }), Ui.Meta, wrap: true));
        }
        if (card.ImageCaption is { } caption) image.AddChild(Ui.Label(caption, Ui.Small, wrap: true));
        if (card.ImageCredit is { } credit) image.AddChild(Ui.Label(credit, Ui.Meta, wrap: true));
        body.AddChild(Ui.Panel(image, 12));

        body.AddChild(Section(labels.DocumentedHistory, "", card.Title));
        body.AddChild(Ui.Label(card.Text, Ui.Small, wrap: true));
        if (card.Credit is { } cardCredit) body.AddChild(Ui.Label(cardCredit, Ui.Meta, wrap: true));

        var comparison = view.Comparison;
        body.AddChild(Section(comparison.Label, "Info", null));
        body.AddChild(Ui.Label(comparison.Intro, Ui.Small, wrap: true));
        foreach (var line in comparison.Lines) body.AddChild(Ui.Label(line.Text, Ui.Small, wrap: true));
        body.AddChild(Ui.Label(comparison.Outro, Ui.Small, wrap: true));

        body.AddChild(Section(labels.FictionalAdaptation, "Accent", null));
        body.AddChild(Ui.Label(view.FictionalAdaptationText, Ui.Small, wrap: true));

        _historyScroll.AddChild(body);
        column.AddChild(_historyScroll);
        return Ui.Panel(column, 16);

        Control Section(string label, string tone, string? title)
        {
            var row = Ui.Row(8);
            row.AddChild(Ui.Badge(label, tone));
            if (title is not null) row.AddChild(Ui.Label(title));
            _historyAnchors[label] = row;
            return row;
        }
    }

    private void JumpTo(string label)
    {
        if (_historyAnchors.TryGetValue(label, out var anchor)) _historyScroll.EnsureControlVisible(anchor);
    }

    private static Texture2D? HistoryImage(DebriefHistoryCardView card, BriefingDefinition? briefing, FrontendContext context)
    {
        // The card's own curated item (mission 1: the early-return recovery photo), else its briefing slot's.
        var nasaId = card.NasaId ?? briefing?.ImageSlots.FirstOrDefault(s => s.Id == card.ImageSlotId)?.NasaId;
        if (nasaId is null) return null;
        var item = context.Manifest.Items
            .Where(i => string.Equals(i.ItemId, nasaId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Shipped)
            .FirstOrDefault();
        return Ui.LoadImage(item?.LocalPath);
    }

    private static Control Sized(Control control, float ratio)
    {
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        control.SizeFlagsStretchRatio = ratio;
        return control;
    }

    private static StyleBoxFlat Tinted(Color background, Color border, UiTokenSet tokens) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = tokens.BorderWidthPx,
        BorderWidthTop = tokens.BorderWidthPx,
        BorderWidthRight = tokens.BorderWidthPx,
        BorderWidthBottom = tokens.BorderWidthPx,
        CornerRadiusTopLeft = tokens.RadiusControlPx,
        CornerRadiusTopRight = tokens.RadiusControlPx,
        CornerRadiusBottomLeft = tokens.RadiusControlPx,
        CornerRadiusBottomRight = tokens.RadiusControlPx,
    };
}
