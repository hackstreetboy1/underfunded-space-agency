using Godot;
using Usa.Game.UI.Theme;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Binding.Comms;
using Usa.Game.UI.Console.Components;

namespace Usa.Game.UI.Console.Comms;

/// <summary>What the player can do on a transcript row; each takes the id the comms controller expects.</summary>
/// <param name="CancelPending">✕ on "… is responding" (request id).</param>
/// <param name="Reply">A clarification's quick reply (its text).</param>
/// <param name="AcceptReadOffer">The crew's offer to read a fact (message id).</param>
/// <param name="EditResend">"Edit &amp; resend" on a player bubble (its text).</param>
/// <param name="EditResendLabel">The button's label; null keeps the scene's.</param>
public sealed record TranscriptRowActions(
    Action<string> CancelPending, Action<string> Reply, Action<string> AcceptReadOffer, Action<string> EditResend, string? EditResendLabel)
{
    /// <summary>Tooltip of the pending line's ✕; null keeps the scene's.</summary>
    public string? CancelTooltip { get; init; }
}

/// <summary>
/// One transcript message (<c>TranscriptRow.tscn</c>, Figma G1-03 nodes 13:369–13:387, G2-08; UI.md 3.5): a right-aligned
/// player bubble with the addressee meta line, a crew bubble with name, MET, badge, fact chips, quick replies and read
/// offer, a centred system line, or the pending "… is responding" line with cancel.
/// </summary>
public sealed partial class TranscriptRowView : HBoxContainer
{
    // Bubble width against the free space beside it: player ≈ 56 %, crew ≈ 83 % of the column (Figma 228 / 408, 340 / 408).
    private const float PlayerBubbleRatio = 1.3f;
    private const float CrewBubbleRatio = 5f;

    private Control _lead = null!;
    private Control _trail = null!;
    private PanelContainer _bubble = null!;
    private Control _head = null!;
    private Label _speaker = null!;
    private Label _met = null!;
    private BadgeView _badge = null!;
    private Label _text = null!;
    private Label _reason = null!;
    private HFlowContainer _facts = null!;
    private HFlowContainer _options = null!;
    private Button _readOffer = null!;
    private Label _meta = null!;
    private Button _editResend = null!;
    private Label _system = null!;
    private Control _pending = null!;
    private Label _pendingText = null!;
    private Button _cancel = null!;
    private TranscriptRowModel? _row;
    private TranscriptRowActions? _actions;

    [Export]
    public PackedScene? FactChipScene { get; set; }

    public override void _Ready()
    {
        _lead = GetNode<Control>("%Lead");
        _trail = GetNode<Control>("%Trail");
        _bubble = GetNode<PanelContainer>("%Bubble");
        _head = GetNode<Control>("%Head");
        _speaker = GetNode<Label>("%Speaker");
        _met = GetNode<Label>("%Met");
        _badge = GetNode<BadgeView>("%Badge");
        _text = GetNode<Label>("%Text");
        _reason = GetNode<Label>("%Reason");
        _facts = GetNode<HFlowContainer>("%Facts");
        _options = GetNode<HFlowContainer>("%Options");
        _readOffer = GetNode<Button>("%ReadOffer");
        _meta = GetNode<Label>("%Meta");
        _editResend = GetNode<Button>("%EditResend");
        _system = GetNode<Label>("%System");
        _pending = GetNode<Control>("%Pending");
        _pendingText = GetNode<Label>("%PendingText");
        _cancel = GetNode<Button>("%Cancel");
        StatusIconView.UseIcon(_cancel, UiIcons.ActionClose);
        _readOffer.Pressed += () =>
        {
            if (_row is { } row) _actions?.AcceptReadOffer(row.Id);
        };
        _editResend.Pressed += () =>
        {
            if (_row is { } row) _actions?.EditResend(row.Text);
        };
        _cancel.Pressed += () =>
        {
            if (_row?.RequestId is { } requestId) _actions?.CancelPending(requestId);
        };
    }

    public override void _ExitTree() => _actions = null;

    public void Bind(TranscriptRowModel row, ConsolePalette palette, TranscriptRowActions actions)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(actions);
        _row = row;
        _actions = actions;

        var layout = row.Layout;
        var inBubble = layout is TranscriptRowLayout.Player or TranscriptRowLayout.Crew;
        _bubble.Visible = inBubble;
        _system.Visible = layout == TranscriptRowLayout.System;
        _pending.Visible = layout == TranscriptRowLayout.Pending;
        _lead.Visible = layout == TranscriptRowLayout.Player;
        _trail.Visible = layout == TranscriptRowLayout.Crew;

        if (layout == TranscriptRowLayout.System)
        {
            _system.Text = row.Text + ConsoleText.Separator + row.Meta;
            return;
        }
        if (layout == TranscriptRowLayout.Pending)
        {
            _pendingText.Text = row.Text;
            _cancel.Visible = row.RequestId is not null;
            return;
        }

        var player = layout == TranscriptRowLayout.Player;
        _bubble.SizeFlagsStretchRatio = player ? PlayerBubbleRatio : CrewBubbleRatio;
        _bubble.AddThemeStyleboxOverride("panel", ConsoleStyles.Fill(palette, palette.Of(player ? ConsolePalette.Bg3 : ColourTokens.Bg2)));
        _head.Visible = !player;
        _speaker.ShowText(row.Speaker);
        _met.Text = row.Meta;
        _badge.Display(row.Badge ?? string.Empty, row.Badge is null ? null : ColourTokens.Info);
        _text.Text = row.Text;
        _reason.ShowText(row.Reason);
        _meta.Visible = player;
        _meta.Text = player ? row.Meta : string.Empty;
        _editResend.Visible = player;
        if (actions.EditResendLabel is { } editLabel) _editResend.Text = editLabel;
        if (actions.CancelTooltip is { } cancelTip) _cancel.TooltipText = cancelTip;
        ShowFacts(row.FactChips);
        ShowOptions(row.Options);
        _readOffer.Visible = row.ReadOfferLabel is not null;
        _readOffer.Text = row.ReadOfferLabel ?? string.Empty;
    }

    private void ShowFacts(IReadOnlyList<string> chips)
    {
        Clear(_facts);
        _facts.Visible = chips.Count > 0 && FactChipScene is not null;
        if (FactChipScene is not { } scene) return;
        foreach (var chip in chips)
        {
            var badge = scene.Instantiate<BadgeView>();
            _facts.AddChild(badge);
            badge.Display(chip, null);
        }
    }

    private void ShowOptions(IReadOnlyList<string> options)
    {
        Clear(_options);
        _options.Visible = options.Count > 0;
        foreach (var option in options)
        {
            var button = new Button { Text = option, ThemeTypeVariation = ThemeVariations.ButtonGhost };
            button.Pressed += () => _actions?.Reply(option);
            _options.AddChild(button);
        }
    }

    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
