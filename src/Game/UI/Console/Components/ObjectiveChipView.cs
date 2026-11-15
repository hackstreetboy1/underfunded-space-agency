using Godot;
using Usa.Game.Presentation.Console.Header;

namespace Usa.Game.UI.Console.Components;

/// <summary>A header objective (<c>ObjectiveChip.tscn</c>, UI.md 3.1): its state shape as an icon and its short label, the state word as tooltip.</summary>
public sealed partial class ObjectiveChipView : HBoxContainer
{
    private Label _text = null!;
    private StatusIconView _icon = null!;

    public override void _Ready()
    {
        _text = GetNode<Label>("%Text");
        _icon = StatusIconView.Create(12);
        AddChild(_icon);
        MoveChild(_icon, 0);
    }

    public void Show(ObjectiveChip chip, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(chip);
        ArgumentNullException.ThrowIfNull(palette);
        var colour = palette.Of(chip.Visual.ColourToken);
        _icon.Show(chip.Visual.Shape, colour);
        _text.Text = chip.Label;
        _text.SetFontColour(colour);
        TooltipText = chip.Visual.Word;
    }
}
