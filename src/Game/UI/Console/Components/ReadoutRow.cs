using Godot;
using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A labelled systems-board readout (<c>ReadoutRow.tscn</c>, Figma G1-02 "Readout" Row layout): value with unit, source
/// and MET meta line, knowledge badge and the "Ask" affordance for unknown values (UI.md 2.3).
/// </summary>
public sealed partial class ReadoutRow : HBoxContainer
{
    private readonly BindingScope _bindings = new();
    private Label _label = null!;
    private Label _value = null!;
    private Label _meta = null!;
    private BadgeView _badge = null!;
    private Button _ask = null!;
    private FactReadoutViewModel? _readout;
    private ConsolePalette _palette = ConsolePalette.Default;
    private ReadoutText? _text;
    private KnowledgeVisual _knowledge = KnowledgeBadges.Unknown;

    /// <summary>The source and MET line (<c>Okafor · MET 06:58</c>), the guided first run's "who read it, when" anchor.</summary>
    public Label SourceStamp => _meta;

    public override void _Ready()
    {
        _label = GetNode<Label>("%Label");
        _value = GetNode<Label>("%Value");
        _meta = GetNode<Label>("%Meta");
        _badge = GetNode<BadgeView>("%Badge");
        _ask = GetNode<Button>("%Ask");
        _ask.Pressed += () => _readout?.Ask();
    }

    public override void _ExitTree() => Unbind();

    public void Bind(FactReadoutViewModel readout, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(readout);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _readout = readout;
        _palette = palette;
        _label.Text = readout.Label;
        _bindings.Observe(readout.Knowledge, knowledge =>
        {
            _knowledge = knowledge;
            Refresh();
        });
        _bindings.Observe(readout.Readout.Text, text =>
        {
            _text = text;
            Refresh();
        });
    }

    public void Unbind()
    {
        _bindings.Release();
        _readout = null;
        _text = null;
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _ask.Text = labels.Ask;
    }

    private void Refresh()
    {
        if (_text is not { } text) return;
        _value.Text = ConsoleText.ReadoutValue(text);
        _value.SetFontColour(_palette.Of(text.Dimmed ? ColourTokens.TextMid : _knowledge.ValueColourToken));
        _meta.ShowText(ConsoleText.ReadoutMeta(text));
        _badge.Display(_knowledge, text.Badge);
        _ask.Visible = text.CanAsk;
    }
}
