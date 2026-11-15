using Godot;
using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// An arc gauge (<c>ArcGauge.tscn</c>; UI.md 3.9 Prop &amp; attitude rate gauges, Figma G1-02 "Arc gauge", G1-03 node 15:470):
/// the dial with the label, value and unit in its centre, the status badge (shape + word) and the source meta line
/// (<c>Brandt · MET 06:57</c>) under it. Only what the ground knows fills the dial.
/// </summary>
public sealed partial class ArcGaugeView : VBoxContainer
{
    private readonly BindingScope _bindings = new();
    private ArcDial _dial = null!;
    private Label _name = null!;
    private Label _value = null!;
    private Label _unit = null!;
    private BadgeView _status = null!;
    private Label _meta = null!;
    private StatusLanguage? _language;
    private ConsolePalette _palette = ConsolePalette.Default;

    public override void _Ready()
    {
        // The dial is a scripted node inside this scene, not an instance.
        _dial = (ArcDial)GetNode<Control>("%Dial");
        _name = GetNode<Label>("%Name");
        _value = GetNode<Label>("%Value");
        _unit = GetNode<Label>("%Unit");
        _status = GetNode<BadgeView>("%Status");
        _meta = GetNode<Label>("%Meta");
    }

    public override void _ExitTree() => Unbind();

    /// <param name="shortLabel">The centre label, <c>ROLL</c>; the readout's label when null.</param>
    public void Bind(FactReadoutViewModel readout, StatusLanguage status, ConsolePalette palette, string? shortLabel = null)
    {
        ArgumentNullException.ThrowIfNull(readout);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _language = status;
        _palette = palette;
        _name.Text = shortLabel ?? readout.Label;
        _bindings.Observe(readout.Parts, parts =>
        {
            _value.Text = parts.SecondaryValue is { } second ? parts.Value + " / " + second : parts.Value;
            _unit.Text = parts.Unit;
        });
        _bindings.Observe(readout.Readout.Text, text => _meta.ShowText(ConsoleText.ReadoutMeta(text)));
        _bindings.Observe(readout.Gauge, ShowReading);
    }

    public void Unbind() => _bindings.Release();

    private void ShowReading(GaugeReading? reading)
    {
        if (_language is not { } language) return;
        var visual = language.For(reading?.Status ?? ConsoleStatus.Unknown);
        _dial.Show(reading, visual.ColourToken, _palette);
        _status.Display(visual);
    }
}
