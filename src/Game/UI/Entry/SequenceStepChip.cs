using Godot;
using Usa.Game.Presentation.EntryHud;
using Usa.Game.UI.Console;

namespace Usa.Game.UI.Entry;

/// <summary>
/// One step of the entry sequence strip (<c>SequenceStepChip.tscn</c>, Figma "Sequence step" Done / Current / Pending in
/// node 34:7721): status icon, label and an optional detail line (the chute rows' time to altitude). The shape follows
/// the step state as an approved icon (R3-11): done check, failed octagon, pending dashed circle, and the active step as
/// the filled circle in the accent tint on its highlighted chip.
/// </summary>
public sealed partial class SequenceStepChip : PanelContainer
{
    private readonly VariationSetter _variations = new();
    private TextureRect _glyph = null!;
    private Label _label = null!;
    private Label _detail = null!;

    public override void _Ready()
    {
        _glyph = GetNode<TextureRect>("%Glyph");
        _label = GetNode<Label>("%Label");
        _detail = GetNode<Label>("%Detail");
    }

    public void Show(SequenceStep step, string? detail, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(palette);
        _variations.Set(this, step.State switch
        {
            SequenceStepState.Active => ThemeVariationNames.BadgePanel("info"),
            SequenceStepState.Failed => ThemeVariationNames.BadgePanel("critical"),
            _ => ThemeVariationNames.Badge,
        });
        var (shape, token) = step.State switch
        {
            SequenceStepState.Active => (HudShape.Nominal, "accent"),
            SequenceStepState.Done => (HudShape.Done, HudToneTokens.Of(step.Tone)),
            SequenceStepState.Failed => (HudShape.Failed, HudToneTokens.Of(step.Tone)),
            _ => (HudShape.Unknown, HudToneTokens.Of(step.Tone)),
        };
        HudIcons.Show(_glyph, shape, palette.Of(token));
        _label.Text = step.Label;
        _label.SetFontColour(palette.Of(step.State == SequenceStepState.Pending ? "text.mid" : "text.hi"));
        _detail.ShowText(detail);
    }
}
