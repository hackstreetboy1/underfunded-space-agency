using Godot;
using Usa.Game.Presentation.Console.EventLog;

namespace Usa.Game.UI.Console.Components;

/// <summary>One event-log row (<c>EventLogRow.tscn</c>, Figma G1-02 "Event log row"; G1-03 node 14:376): MET · category · text · source.</summary>
public sealed partial class EventLogRow : HBoxContainer
{
    private Label _met = null!;
    private Label _category = null!;
    private Label _text = null!;
    private Label _source = null!;

    public override void _Ready()
    {
        _met = GetNode<Label>("%Met");
        _category = GetNode<Label>("%Category");
        _text = GetNode<Label>("%Text");
        _source = GetNode<Label>("%Source");
    }

    public void Bind(LogRow row, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(palette);
        _met.Text = row.MetText;
        _category.Text = row.CategoryWord;
        _category.SetFontColour(palette.Of(row.ColourToken));
        _text.Text = row.Text;
        _source.Text = row.Source;
    }
}
