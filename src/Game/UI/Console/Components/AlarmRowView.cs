using Godot;
using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// One alarm (<c>AlarmRow.tscn</c>, Figma G1-02 "Alarm row" Advisory / Caution / Warning / Master; G1-03 node 15:452):
/// shape, priority word, raise MET, text, source and ACK (UI.md 3.10). Acknowledging never fixes anything.
/// </summary>
public sealed partial class AlarmRowView : PanelContainer
{
    private StatusIconView _glyph = null!;
    private Label _priority = null!;
    private Label _met = null!;
    private Label _text = null!;
    private Label _source = null!;
    private readonly BindingScope _bindings = new();
    private Button _ack = null!;
    private AlarmListViewModel? _alarms;
    private string? _alarmId;

    public override void _Ready()
    {
        // The shape is an approved icon in the scene's 16 px slot (review R3-11).
        _glyph = StatusIconView.Create(16);
        GetNode<Control>("%Glyph").AddChild(_glyph);
        _priority = GetNode<Label>("%Priority");
        _met = GetNode<Label>("%Met");
        _text = GetNode<Label>("%Text");
        _source = GetNode<Label>("%Source");
        _ack = GetNode<Button>("%Ack");
        _ack.Pressed += () =>
        {
            if (_alarms is { } alarms && _alarmId is { } id) alarms.Acknowledge(id);
        };
    }

    public override void _ExitTree()
    {
        _bindings.Release();
        _alarms = null;
    }

    /// <param name="alarms">ACK acknowledges through the list: this alarm when the session can acknowledge one, every active
    /// alarm otherwise, and the button says which (<c>ACK</c> or <c>ACK ALL</c>).</param>
    public void Bind(AlarmRow row, ConsolePalette palette, AlarmListViewModel alarms)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(alarms);
        _alarmId = row.AlarmId;
        if (!ReferenceEquals(_alarms, alarms))
        {
            _bindings.Release();
            _alarms = alarms;
            _bindings.Observe(alarms.AckWord, word =>
            {
                if (word.Length > 0) _ack.Text = word;
            });
            _bindings.Observe(alarms.AckTooltip, tip =>
            {
                if (tip.Length > 0) _ack.TooltipText = tip;
            });
        }

        this.SetVariation(ThemeVariations.AlarmRowPanel(row.Priority));
        var onFill = row.Priority == AlarmPriority.Master;
        var textStyle = ThemeVariations.AlarmRowText(row.Priority);
        _text.SetVariation(textStyle);
        _source.SetVariation(onFill ? textStyle : ThemeVariations.Small);
        _met.SetVariation(onFill ? textStyle : ThemeVariations.ReadoutSmall);
        _priority.SetVariation(onFill ? textStyle : ThemeVariations.BadgeText(row.Visual.ColourToken));
        // On the solid master fill the shape takes the dark on-fill colour, like the text.
        _glyph.Show(row.Visual.Shape, onFill ? palette.Tokens.ColorBg0 : palette.Of(row.Visual.ColourToken));
        _priority.Text = row.Visual.Word;
        _met.Text = row.MetText;
        _text.Text = row.Text;
        _source.ShowText(row.Source);
        _ack.Visible = !row.Acknowledged;
    }
}
