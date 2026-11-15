using Godot;
using Usa.Game.Content;
using Usa.Game.Presentation.EntryHud;
using Usa.Game.UI.Console;

namespace Usa.Game.UI.Entry;

/// <summary>
/// The splashdown / recovery card (<c>res://ui/entry/RecoveryCard.tscn</c>, 520 wide; Figma G4-04 node 35:3670, component
/// "Recovery card" Waiting / Recovered in 34:7721), centred over the External view with no scrim while the sim runs.
/// "Proceed to debrief" is enabled once the crew is recovered; the host time-compresses while
/// <see cref="SplashdownCardModel.CompressTime"/> holds. Its words come from <c>map.recovery*</c> strings
/// (<see cref="ApplyText"/>); until the recovery forces report, the zone badge is neutral, never a caution.
/// </summary>
public sealed partial class RecoveryCardView : PanelContainer
{
    private static readonly TextSlot[] Slots =
    [
        new("Column/Body/Rows/ZoneTitle", "map.recoveryZoneTitle"),
        new("%WaitBadge/Text", "map.badgeEstimated"),
        new("Column/Body/Rows/RemainingRow/Title", "map.recoveryForces"),
        new("%Compression/Text", "map.recoveryCompression"),
        new("Column/Body/Rows/CrewTitle", "map.recoveryCrew"),
        new("%ProceedButton", "map.recoveryProceed"),
    ];

    private readonly VariationSetter _variations = new();

    private Label _title = null!;
    private Label _zone = null!;
    private PanelContainer _zoneBadge = null!;
    private Label _zoneBadgeText = null!;
    private Control _distanceRow = null!;
    private Label _distanceTitle = null!;
    private Label _distance = null!;
    private Label _waitTitle = null!;
    private Label _waitEstimate = null!;
    private Control _waitBadge = null!;
    private RecoveryTrack _track = null!;
    private Label _remaining = null!;
    private Control _compression = null!;
    private VBoxContainer _crewRows = null!;
    private Label _crewSummary = null!;
    private Label _footerNote = null!;
    private Button _proceed = null!;
    private ConsolePalette _palette = ConsolePalette.Default;
    private CardWords _words = CardWords.Design;

    /// <summary>"Proceed to debrief" was pressed (only possible once recovered).</summary>
    public event Action? ProceedRequested;

    /// <summary><c>CrewMiniRow.tscn</c>, set in the scene.</summary>
    [Export]
    public PackedScene? CrewScene { get; set; }

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _zone = GetNode<Label>("%Zone");
        _zoneBadge = GetNode<PanelContainer>("%ZoneBadge");
        _zoneBadgeText = GetNode<Label>("%ZoneBadge/Text");
        _distanceRow = GetNode<Control>("%DistanceRow");
        _distanceTitle = _distanceRow.GetNode<Label>("Title");
        _distance = GetNode<Label>("%Distance");
        _waitTitle = GetNode<Label>("%WaitTitle");
        _waitEstimate = GetNode<Label>("%WaitEstimate");
        _waitBadge = GetNode<Control>("%WaitBadge");
        _track = GetNode<RecoveryTrack>("%Track");
        _remaining = GetNode<Label>("%Remaining");
        _compression = GetNode<Control>("%Compression");
        _crewRows = GetNode<VBoxContainer>("%CrewRows");
        _crewSummary = GetNode<Label>("%CrewSummary");
        _footerNote = GetNode<Label>("%FooterNote");
        _proceed = GetNode<Button>("%ProceedButton");
        _proceed.Pressed += () => ProceedRequested?.Invoke();
    }

    /// <summary>The card's words from <c>content/strings.json</c>, once per mission.</summary>
    public void ApplyText(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _words = CardWords.From(text);
        if (IsNodeReady()) SceneText.Apply(this, text, Slots);
    }

    public void SetPalette(ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
    }

    /// <summary>Shows the card while <see cref="SplashdownCardModel.Visible"/>; hides it otherwise.</summary>
    public void Show(SplashdownCardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        Visible = card.Visible;
        if (!card.Visible) return;
        var words = _words;
        var recovered = card.ProceedEnabled;
        _title.Text = recovered ? words.RecoveredTitle : words.SplashdownTitle;
        _zone.Text = card.ZoneText;
        // No report yet is not a verdict: neutral badge rather than an OUTSIDE ZONES caution.
        string? zoneTone = card.AwaitingReport ? null : card.InZone ? "ok" : "caution";
        _variations.SetBadge(_zoneBadge, _zoneBadgeText, zoneTone);
        _zoneBadgeText.Text = card.ZoneBadgeText.Length > 0 ? card.ZoneBadgeText
            : card.AwaitingReport ? words.Awaiting
            : card.InZone ? words.InZone
            : words.OutsideZones;
        _zoneBadge.Visible = _zoneBadgeText.Text.Length > 0;
        ShowDistanceOrSplashPoint(card, words);

        _waitTitle.Text = recovered ? words.RecoveryComplete : words.RecoveryWait;
        _waitEstimate.Text = card.WaitEstimateText;
        _waitBadge.Visible = !recovered && card.WaitEstimateText != EntryHudFormat.Placeholder;
        _track.Show(card.RecoveryProgress01, _palette.Of("info"), _palette.Tokens.ColorBg3, _palette.Of("text.hi"));
        _remaining.Text = card.WaitRemainingText;
        _compression.Visible = card.CompressTime;

        ChildSync.Sync(_crewRows, card.Crew.Count, CrewScene);
        for (var i = 0; i < card.Crew.Count; i++) _crewRows.GetChild<CrewMiniRowView>(i).Show(card.Crew[i], _palette);
        _crewSummary.Text = card.CrewSummary;

        _proceed.Disabled = !card.ProceedEnabled;
        _proceed.TooltipText = card.ProceedEnabled ? string.Empty : words.Waiting;
        _footerNote.Text = recovered ? words.Done : words.Waiting;
    }

    /// <summary>
    /// The scene's one detail row: distance to the ship when known, else the reported splash point and MET (G4-04
    /// "Splash point 135° E"); hidden when neither was reported.
    /// </summary>
    private void ShowDistanceOrSplashPoint(SplashdownCardModel card, CardWords words)
    {
        if (card.DistanceText != EntryHudFormat.Placeholder || card.SplashPointText.Length == 0)
        {
            _distanceRow.Visible = card.DistanceText != EntryHudFormat.Placeholder;
            _distanceTitle.Text = words.ToShip;
            _distance.Text = card.DistanceText;
            return;
        }
        _distanceRow.Visible = true;
        _distanceTitle.Text = card.SplashPointTitle;
        _distance.Text = card.SplashMetText.Length > 0 ? $"{card.SplashPointText} · {card.SplashMetText}" : card.SplashPointText;
    }

    /// <summary>The words that change with the card's state.</summary>
    private sealed record CardWords(
        string SplashdownTitle,
        string RecoveredTitle,
        string InZone,
        string OutsideZones,
        string Awaiting,
        string ToShip,
        string RecoveryWait,
        string RecoveryComplete,
        string Waiting,
        string Done)
    {
        public static CardWords From(ITextSource text) => new(
            text.Get("map.recoverySplashdownTitle"),
            text.Get("map.recoveryRecoveredTitle"),
            text.Get("map.zoneInZone"),
            text.Get("map.outsideZones"),
            text.Get("map.recoveryAwaitingBadge"),
            text.Get("map.recoveryToShip"),
            text.Get("map.recoveryWait"),
            text.Get("map.recoveryComplete"),
            text.Get("map.recoveryWaiting"),
            text.Get("map.recoveryDone"));

        /// <summary>The design words (the same as the scene's), until the host applies the mission's strings.</summary>
        public static CardWords Design { get; } = new(
            "SPLASHDOWN", "RECOVERED", "IN ZONE", "OUTSIDE ZONES", "AWAITING REPORT", "To recovery ship", "Estimated recovery wait",
            "Recovery complete", "Available when the crew is aboard the ship.", "Mission complete. The debrief shows what you knew at each decision.");
    }
}
