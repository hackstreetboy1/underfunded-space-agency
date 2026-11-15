using Godot;
using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A titled group of readouts on a systems-board tab (<c>ReadoutGroup.tscn</c>, Figma G1-03 node 15:594). A readout with a
/// gauge scale is drawn as an arc gauge in the gauges row (the Prop &amp; attitude rates, node 15:470), the others as rows.
/// </summary>
public sealed partial class ReadoutGroupView : VBoxContainer
{
    private readonly BindingScope _bindings = new();
    private Label _title = null!;
    private VBoxContainer _rows = null!;
    private HBoxContainer _gauges = null!;

    [Export]
    public PackedScene? RowScene { get; set; }

    [Export]
    public PackedScene? GaugeScene { get; set; }

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _rows = GetNode<VBoxContainer>("%Rows");
        _gauges = GetNode<HBoxContainer>("%Gauges");
    }

    public override void _ExitTree() => Unbind();

    public void Bind(ReadoutGroupViewModel group, StatusLanguage status, ConsolePalette palette, ConsoleLabels? labels)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _bindings.Observe(group.Title, title => _title.Text = title);
        // Each readout gets a row and a gauge; only the one that fits its kind is shown.
        if (RowScene is { } rowScene)
        {
            _bindings.Mirror(group.Readouts, new ListBinder<FactReadoutViewModel, ReadoutRow>(_rows, rowScene, (row, readout) =>
            {
                row.Bind(readout, palette);
                if (labels is not null) row.ApplyLabels(labels);
                row.Visible = readout.GaugeScale is null;
            }));
        }
        if (GaugeScene is { } gaugeScene)
        {
            _bindings.Mirror(group.Readouts, new ListBinder<FactReadoutViewModel, ArcGaugeView>(_gauges, gaugeScene, (gauge, readout) =>
            {
                gauge.Bind(readout, status, palette);
                gauge.Visible = readout.GaugeScale is not null;
            }));
        }
        _bindings.Add(group.Readouts.Subscribe(_ => RefreshGaugeRow(group)));
        RefreshGaugeRow(group);
    }

    public void Unbind() => _bindings.Release();

    /// <summary>The source and MET stamp of the first readout on screen, for the guided first run; null when none shows.</summary>
    public Control? FirstVisibleSourceStamp()
    {
        for (var i = 0; i < _rows.GetChildCount(); i++)
        {
            if (_rows.GetChild(i) is ReadoutRow { Visible: true } row && row.SourceStamp.IsVisibleInTree()) return row.SourceStamp;
        }
        return null;
    }

    private void RefreshGaugeRow(ReadoutGroupViewModel group)
    {
        var any = false;
        foreach (var readout in group.Readouts) any |= readout.GaugeScale is not null;
        _gauges.Visible = any;
    }
}
