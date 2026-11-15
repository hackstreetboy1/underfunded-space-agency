using Godot;
using Usa.Game.Presentation.Console.Tips;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>An inline fact chip in the tip card (<c>FactChip.tscn</c>, Figma G1-03 node 13:295); a click highlights the source panel.</summary>
public sealed partial class FactChipButton : Button
{
    private FactChip? _chip;
    private Action<FactChip>? _highlight;

    public override void _Ready() => Pressed += () =>
    {
        if (_chip is { } chip) _highlight?.Invoke(chip);
    };

    public override void _ExitTree() => _highlight = null;

    public void Bind(FactChip chip, Action<FactChip> highlight)
    {
        ArgumentNullException.ThrowIfNull(chip);
        ArgumentNullException.ThrowIfNull(highlight);
        _chip = chip;
        _highlight = highlight;
        Text = ConsoleText.FactChip(chip.Label, chip.Badge);
    }
}
