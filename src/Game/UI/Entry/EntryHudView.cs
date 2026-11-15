using System.Globalization;
using Godot;
using Usa.Game.Content;
using Usa.Game.Presentation.EntryHud;
using Usa.Game.UI.Console;

namespace Usa.Game.UI.Entry;

/// <summary>
/// The entry and landing HUD over the External view during ENTRY (<c>res://ui/entry/EntryHud.tscn</c>, the main-view
/// region, transparent and click-through; Figma G4-03 node 34:6170). Top left: altitude, velocity, flight-path angle, g gauge,
/// heating bar and time to drogue/main, each with source, MET and state (X8). Top right: crew health. Bottom: Entry
/// Checklist progress and the sequence strip. The host calls <see cref="Show"/> at 10 Hz with the runtime's model, and
/// <see cref="ApplyText"/> once with the strings (<c>map.entry*</c>).
/// </summary>
public sealed partial class EntryHudView : Control
{
    // Design text for previews that bind no strings; the mission uses map.entryTelemetry / map.entryHeatingMarks.
    private const string DesignTelemetry = "telemetry · {time}";
    private const string DesignHeatingMarks = "Caution above {caution} % · critical above {critical} %";

    private static readonly TextSlot[] Slots =
    [
        new("EntryPanel/Pad/Rows/Header/Title", "map.entryTitle"),
        new("%DrogueBadge/Text", "map.badgeEstimated"),
        new("%MainBadge/Text", "map.badgeEstimated"),
        new("CrewPanel/Pad/Rows/Title", "map.entryCrew"),
        new("Bottom/Checklist/Pad/Row/Title", "map.entryChecklist"),
    ];

    private Label _telemetryAside = null!;
    private HudReadoutRowView _altitude = null!;
    private HudReadoutRowView _velocity = null!;
    private HudReadoutRowView _flightPathAngle = null!;
    private HudReadoutRowView _gLoad = null!;
    private HudBarGauge _gGauge = null!;
    private HudReadoutRowView _heating = null!;
    private HudBarGauge _heatingGauge = null!;
    private Label _heatingMarks = null!;
    private Label _drogueTitle = null!;
    private Label _drogueText = null!;
    private Control _drogueBadge = null!;
    private Label _mainTitle = null!;
    private Label _mainText = null!;
    private Control _mainBadge = null!;
    private VBoxContainer _crewRows = null!;
    private Control _checklist = null!;
    private Label _checklistText = null!;
    private ProgressBar _checklistBar = null!;
    private HBoxContainer _sequence = null!;
    private ConsolePalette _palette = ConsolePalette.Default;
    private EntryHudLimits _limits = EntryHudLimits.Default;
    private string _telemetryTemplate = DesignTelemetry;
    private string _heatingMarksTemplate = DesignHeatingMarks;

    /// <summary><c>SequenceStepChip.tscn</c>, set in the scene.</summary>
    [Export]
    public PackedScene? StepScene { get; set; }

    /// <summary><c>CrewMiniRow.tscn</c>, set in the scene.</summary>
    [Export]
    public PackedScene? CrewScene { get; set; }

    public override void _Ready()
    {
        _telemetryAside = GetNode<Label>("%TelemetryAside");
        _altitude = GetNode<HudReadoutRowView>("%Altitude");
        _velocity = GetNode<HudReadoutRowView>("%Velocity");
        _flightPathAngle = GetNode<HudReadoutRowView>("%FlightPathAngle");
        _gLoad = GetNode<HudReadoutRowView>("%GLoad");
        _gGauge = GetNode<HudBarGauge>("%GGauge");
        _heating = GetNode<HudReadoutRowView>("%Heating");
        _heatingGauge = GetNode<HudBarGauge>("%HeatingGauge");
        _heatingMarks = GetNode<Label>("%HeatingMarks");
        _drogueTitle = GetNode<Label>("%DrogueTitle");
        _drogueText = GetNode<Label>("%DrogueText");
        _drogueBadge = GetNode<Control>("%DrogueBadge");
        _mainTitle = GetNode<Label>("%MainTitle");
        _mainText = GetNode<Label>("%MainText");
        _mainBadge = GetNode<Control>("%MainBadge");
        _crewRows = GetNode<VBoxContainer>("%CrewRows");
        _checklist = GetNode<Control>("%Checklist");
        _checklistText = GetNode<Label>("%ChecklistText");
        _checklistBar = GetNode<ProgressBar>("%ChecklistBar");
        _sequence = GetNode<HBoxContainer>("%Sequence");
        _heating.Title = HeatingBar.Title;
    }

    /// <summary>The HUD's fixed words from <c>content/strings.json</c>, once per mission.</summary>
    public void ApplyText(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _telemetryTemplate = text.Get("map.entryTelemetry");
        _heatingMarksTemplate = text.Get("map.entryHeatingMarks");
        if (!IsNodeReady()) return;
        SceneText.Apply(this, text, Slots);
        _altitude.Title = text.Get("map.entryAltitude");
        _velocity.Title = text.Get("map.entryVelocity");
        _flightPathAngle.Title = text.Get("map.entryFlightPathAngle");
        _gLoad.Title = text.Get("map.entryGLoad");
        _heating.Title = text.Get("map.entryHeating");
    }

    /// <summary>Deploy altitudes, crew g marks and heating thresholds (<c>IEntryHudSource.Limits</c>), normally once per mission.</summary>
    public void SetLimits(EntryHudLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits;
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
    }

    public void Show(EntryHudModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _telemetryAside.ShowText(model.GLoad.Readout.MetText.Length > 0
            ? EntryHudWords.Fill(_telemetryTemplate, ("time", model.GLoad.Readout.MetText))
            : null);
        _altitude.Show(model.Altitude, _palette);
        _velocity.Show(model.Velocity, _palette);
        _flightPathAngle.Show(model.Corridor is { LineText.Length: > 0 } corridor
            ? model.FlightPathAngle with { Meta = Joined(corridor.LineText, corridor.BadgeText, model.FlightPathAngle.Meta) }
            : model.FlightPathAngle, _palette);
        _gLoad.Show(model.GLoad.PeakText.Length > 0
            ? model.GLoad.Readout with { Meta = Joined(model.GLoad.Readout.Meta, model.GLoad.PeakText) }
            : model.GLoad.Readout, _palette);
        _heating.Show(model.Heating.Readout, _palette);

        var marks = new List<(double, Color, string)>(model.GLoad.Marks.Count);
        foreach (var mark in model.GLoad.Marks) marks.Add((mark.Fraction01, _palette.Of(HudToneTokens.Of(mark.Tone)), mark.Label));
        if (model.GLoad.PeakValue is not null) marks.Add((model.GLoad.PeakFraction01, _palette.Of("text.hi"), string.Empty));
        _gGauge.Show(model.GLoad.Fraction01, _palette.Of(HudToneTokens.Of(model.GLoad.Tone)), _palette.Tokens.ColorBg3, marks);

        _heatingGauge.Show(model.Heating.Fraction01, _palette.Of(HudToneTokens.Of(model.Heating.Tone)), _palette.Tokens.ColorBg3,
        [
            (_limits.HeatingCaution01, _palette.Of("caution"), string.Empty),
            (_limits.HeatingCritical01, _palette.Of("critical"), string.Empty),
        ]);
        _heatingMarks.Text = EntryHudWords.Fill(_heatingMarksTemplate,
            ("caution", (_limits.HeatingCaution01 * 100).ToString("0", CultureInfo.InvariantCulture)),
            ("critical", (_limits.HeatingCritical01 * 100).ToString("0", CultureInfo.InvariantCulture)));

        ShowChute(model.Drogue, _drogueTitle, _drogueText, _drogueBadge);
        ShowChute(model.Main, _mainTitle, _mainText, _mainBadge);
        ShowCrew(model.Crew);

        _checklist.Visible = model.Checklist is not null;
        if (model.Checklist is { } checklist)
        {
            _checklistText.Text = checklist.Text;
            _checklistBar.Value = checklist.Fraction01 * 100;
        }
        ShowSequence(model);
    }

    private static string Joined(params string[] parts) => string.Join(" · ", parts.Where(p => p.Length > 0));

    private void ShowChute(TimeToAltitude chute, Label title, Label text, Control badge)
    {
        title.Text = $"{chute.Label} · {chute.TargetText}";
        text.Text = chute.Text;
        text.SetFontColour(_palette.Of(HudToneTokens.Of(chute.Tone)));
        badge.Visible = chute.IsEstimate;
    }

    private void ShowCrew(IReadOnlyList<CrewMini> crew)
    {
        ChildSync.Sync(_crewRows, crew.Count, CrewScene);
        for (var i = 0; i < crew.Count; i++) _crewRows.GetChild<CrewMiniRowView>(i).Show(crew[i], _palette);
    }

    private void ShowSequence(EntryHudModel model)
    {
        ChildSync.Sync(_sequence, model.Sequence.Count, StepScene);
        for (var i = 0; i < model.Sequence.Count; i++)
        {
            var step = model.Sequence[i];
            var countdown = step.Step switch
            {
                EntryStep.Drogue => $"{model.Drogue.Text} · {model.Drogue.TargetText}",
                EntryStep.Main => $"{model.Main.Text} · {model.Main.TargetText}",
                _ => null,
            };
            var time = step.TimeText.Length > 0 ? step.TimeText : null;
            var detail = step.State is SequenceStepState.Done or SequenceStepState.Failed ? time : countdown ?? time;
            _sequence.GetChild<SequenceStepChip>(i).Show(step, detail, _palette);
        }
    }
}
