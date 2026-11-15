using Godot;
using Usa.Game.Presentation.EntryHud;
using Usa.Game.UI.Console;
using Usa.Game.UI.Frontend;
using Usa.Game.UI.Frontend.Model;

namespace Usa.Game.UI.Entry;

/// <summary>
/// A crew health mini-bar (<c>CrewMiniRow.tscn</c>, Figma "Crew health mini" in node 34:7721): name, bar, percent, shape
/// and band word. The shape is an approved icon, or the drawn hollow circle for a deceased crew member (R3-11).
/// </summary>
public sealed partial class CrewMiniRowView : HBoxContainer
{
    private Label _name = null!;
    private HudBarGauge _bar = null!;
    private Label _percent = null!;
    private TextureRect _bandIcon = null!;
    private StatusGlyph _bandRing = null!;
    private Label _band = null!;

    public override void _Ready()
    {
        _name = GetNode<Label>("%Name");
        _bar = GetNode<HudBarGauge>("%Bar");
        _percent = GetNode<Label>("%Percent");
        _bandIcon = GetNode<TextureRect>("%BandIcon");
        _band = GetNode<Label>("%Band");
        _bandRing = StatusGlyph.Create(DebriefTone.Neutral, Colors.White);
        _bandRing.Visible = false;
        AddChild(_bandRing);
        MoveChild(_bandRing, _bandIcon.GetIndex() + 1);
    }

    public void Show(CrewMini crew, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(palette);
        var colour = palette.Of(HudToneTokens.Of(crew.Tone));
        _name.Text = crew.Name;
        _bar.Show(crew.Fraction01, colour, palette.Tokens.ColorBg3);
        _percent.Text = crew.HealthText;
        var shape = HudToneTokens.BandShape(crew.Band);
        _bandIcon.Visible = shape is not null;
        _bandRing.Visible = shape is null;
        if (shape is { } drawn) HudIcons.Show(_bandIcon, drawn, colour);
        else _bandRing.Set(DebriefTone.Neutral, colour);
        _band.Text = crew.BandText.ToUpperInvariant();
        _band.SetFontColour(colour);
    }
}
