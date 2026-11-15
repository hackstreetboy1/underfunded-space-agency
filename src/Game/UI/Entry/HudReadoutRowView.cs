using Godot;
using Usa.Game.Presentation.EntryHud;
using Usa.Game.UI.Console;

namespace Usa.Game.UI.Entry;

/// <summary>
/// One entry HUD readout (<c>HudReadoutRow.tscn</c>): title, value, state badge (<c>EST</c>, <c>STALE 12s</c>,
/// <c>UNKNOWN</c>) and the <c>source · MET</c> meta line (X8; UI.md 2.3). The title is set in the scene.
/// </summary>
public sealed partial class HudReadoutRowView : VBoxContainer
{
    private Label _title = null!;
    private Label _value = null!;
    private PanelContainer _badge = null!;
    private Label _badgeText = null!;
    private Label _meta = null!;
    private string _titleText = string.Empty;
    private readonly VariationSetter _variations = new();

    [Export]
    public string Title
    {
        get => _titleText;
        set
        {
            _titleText = value ?? string.Empty;
            if (IsNodeReady()) _title.Text = _titleText;
        }
    }

    /// <summary>Readout L for the headline values (altitude, velocity, flight-path angle); Readout M otherwise.</summary>
    [Export]
    public bool Large { get; set; }

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _value = GetNode<Label>("%Value");
        _badge = GetNode<PanelContainer>("%Badge");
        _badgeText = GetNode<Label>("%Badge/Text");
        _meta = GetNode<Label>("%Meta");
        _title.Text = _titleText;
        if (Large) _value.ThemeTypeVariation = ThemeVariationNames.ReadoutLarge;
    }

    public void Show(HudReadoutRow row, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(palette);
        _value.Text = row.Text;
        _value.SetFontColour(palette.Of(HudToneTokens.ValueOf(row.State)));
        var tone = HudToneTokens.BadgeOf(row.State);
        _badge.Visible = row.Badge.Length > 0;
        _variations.SetBadge(_badge, _badgeText, tone);
        _badgeText.Text = row.Badge;
        _meta.ShowText(row.Meta);
    }
}
