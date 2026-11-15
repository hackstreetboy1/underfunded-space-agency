using Godot;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A row of the systems board's remote controls (<c>RemoteControlRow.tscn</c>, Figma G1-03 nodes 15:620, 15:628, 15:641):
/// label, the disabled reason, the crew-action marker for crew-only controls and the <see cref="RemoteControl"/>.
/// </summary>
public sealed partial class RemoteControlRow : HBoxContainer
{
    private readonly BindingScope _bindings = new();
    private Label _label = null!;
    private Label _sub = null!;
    private Control _crewMarker = null!;
    private Label _crewMarkerText = null!;
    private RemoteControl _control = null!;

    public override void _Ready()
    {
        _label = GetNode<Label>("%Label");
        _sub = GetNode<Label>("%Sub");
        _crewMarker = GetNode<Control>("%CrewMarker");
        _crewMarkerText = GetNode<Label>("%CrewMarkerText");
        _control = GetNode<RemoteControl>("%Control");
    }

    public override void _ExitTree() => Unbind();

    public void Bind(RemoteControlViewModel control, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _bindings.Observe(control.Label, label => _label.Text = label);
        _bindings.Observe(control.SubLine, _sub.ShowText);
        _bindings.Observe(control.IsCrewOnly, crewOnly =>
        {
            _crewMarker.Visible = crewOnly;
            // Crew-only controls are greyed (UI.md 3.9).
            if (crewOnly) _label.SetFontColour(palette.Of(ColourTokens.TextOff));
            else _label.ClearFontColour();
        });
        _control.Bind(control);
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _crewMarkerText.Text = labels.ControlCrewMarker;
        _crewMarker.TooltipText = labels.ControlCrewMarkerTooltip;
        _control.ApplyLabels(labels);
    }

    public void Unbind()
    {
        _bindings.Release();
        _control.Unbind();
    }
}
