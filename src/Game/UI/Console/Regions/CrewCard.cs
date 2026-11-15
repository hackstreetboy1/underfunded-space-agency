using Godot;
using Usa.Game.Presentation.Console.Crew;
using Usa.Game.Presentation.Views;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;

namespace Usa.Game.UI.Console.Regions;

/// <summary>
/// A crew card (<c>CrewCard.tscn</c>, Figma G1-03 nodes 13:302 and 13:336; UI.md 3.4): the live feed slot with its name tag,
/// name, role, health bar with percent and band, activity, non-nominal state markers, speaking indicator and Talk.
/// </summary>
public sealed partial class CrewCard : PanelContainer
{
    private readonly BindingScope _bindings = new();
    private Label _nameTag = null!;
    private Label _name = null!;
    private SpeakingMeter _speaking = null!;
    private Label _fatigueText = null!;
    private Label _hydrationText = null!;
    private Label _role = null!;
    private Control _healthRow = null!;
    private ProgressBar _health = null!;
    private Label _healthPercent = null!;
    private BadgeView _healthBand = null!;
    private Label _activity = null!;
    private Control _fatigue = null!;
    private Control _hydration = null!;
    private Label _lost = null!;
    private Button _talk = null!;
    private CrewCardViewModel? _card;
    private Control _feedPlaceholder = null!;
    private CrewFeedView? _feed;
    private bool _showRole = true;
    private string _nameText = string.Empty;
    private string _roleText = string.Empty;

    /// <summary>
    /// Where the crew member's live feed goes (UI.md 3.4: a SubViewport, <c>CrewFeed.tscn</c>, owned by the crew-feed
    /// work); sized by the layout. Holds a placeholder label until a feed is attached.
    /// </summary>
    public Control FeedSlot { get; private set; } = null!;

    public override void _Ready()
    {
        FeedSlot = GetNode<Control>("%FeedSlot");
        _feedPlaceholder = GetNode<Control>("%FeedPlaceholder");
        _nameTag = GetNode<Label>("%NameTag");
        _name = GetNode<Label>("%Name");
        _speaking = GetNode<SpeakingMeter>("%Speaking");
        _fatigueText = GetNode<Label>("%FatigueText");
        _hydrationText = GetNode<Label>("%HydrationText");
        _role = GetNode<Label>("%Role");
        _healthRow = GetNode<Control>("%HealthRow");
        _health = GetNode<ProgressBar>("%Health");
        _healthPercent = GetNode<Label>("%HealthPercent");
        _healthBand = GetNode<BadgeView>("%HealthBand");
        _activity = GetNode<Label>("%Activity");
        _fatigue = GetNode<Control>("%Fatigue");
        _hydration = GetNode<Control>("%Hydration");
        _lost = GetNode<Label>("%Lost");
        _talk = GetNode<Button>("%Talk");
        _talk.Pressed += () => _card?.Talk();
    }

    public override void _ExitTree() => Unbind();

    public void Bind(CrewCardViewModel card, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _card = card;
        // No playback level wired yet: the meter shows its still shape while the crew member speaks.
        _speaking.Configure(null, palette);
        _bindings.Observe(card.Name, name =>
        {
            _nameText = name;
            _name.Text = name;
            _nameTag.Text = ConsoleText.NameTag(_nameText, _roleText);
        });
        _bindings.Observe(card.Role, role =>
        {
            _roleText = role;
            _role.Text = role;
            _role.Visible = _showRole && role.Length > 0;
            _nameTag.Text = ConsoleText.NameTag(_nameText, _roleText);
        });
        _bindings.Observe(card.NameColourToken, token => _name.SetFontColour(palette.Of(token)));
        _bindings.Observe(card.Health, health =>
        {
            _health.Value = (health.Fraction ?? 0) * _health.MaxValue;
            _healthPercent.Text = health.PercentText;
            _healthBand.Display(health.Visual);
        });
        _bindings.Observe(card.Activity, _activity.ShowText);
        _bindings.Observe(card.IsSpeaking, _speaking.SetSpeaking);
        _bindings.Observe(card.ShowFatigueIcon, show => _fatigue.Visible = show);
        _bindings.Observe(card.ShowHydrationIcon, show => _hydration.Visible = show);
        _bindings.Observe(card.LostText, _lost.ShowText);
        _bindings.Observe(card.IsDeceased, deceased =>
        {
            _healthRow.Visible = !deceased;
            _talk.Visible = !deceased;
            FeedSlot.Visible = !deceased;
        });
    }

    public void Unbind()
    {
        _bindings.Release();
        _card = null;
    }

    /// <summary>The live feed shown in <see cref="FeedSlot"/>; null when none is attached.</summary>
    public CrewFeedView? Feed => _feed;

    /// <summary>Puts a crew feed into the slot (filling it), replacing any earlier one.</summary>
    public void AttachFeed(CrewFeedView feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (_feed is { } old && GodotObject.IsInstanceValid(old))
        {
            FeedSlot.RemoveChild(old);
            old.QueueFree();
        }
        _feed = feed;
        FeedSlot.AddChild(feed);
        feed.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _feedPlaceholder.Visible = false;
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _talk.Text = labels.CrewTalk;
        _speaking.TooltipText = labels.CrewSpeaking;
        _fatigueText.Text = labels.CrewFatigue;
        _hydrationText.Text = labels.CrewHydration;
    }

    public void ApplyLayout(ConsoleLayoutMetrics layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        CustomMinimumSize = new Vector2(0, layout.CrewCardPx);
        FeedSlot.CustomMinimumSize = new Vector2(layout.CrewFeedWidthPx, layout.CrewFeedHeightPx);
        _showRole = layout.ShowCrewRole;
        _role.Visible = _showRole && _roleText.Length > 0;
    }
}
