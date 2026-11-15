using Godot;
using Usa.Game.Presentation.Workspace;
using Usa.Game.UI.Console;
using Usa.Game.UI.Entry;
using Usa.Simulation.Engineering;

namespace Usa.Game.UI.Workspace;

/// <summary>
/// One check of the digital test (<c>CheckRow.tscn</c>, Figma "Check row" Pass/Fail × Normal/Easy in node 36:242). Normal
/// shows the check id with PASS/FAIL (Gate 5 decision 2, Plex Sans 12 per decision 3); Easy shows the plain name and one
/// line on what was found, never the fix. The mark is an approved icon by status (R3-11), not the model's text glyph.
/// </summary>
public sealed partial class CheckRowView : PanelContainer
{
    private readonly VariationSetter _variations = new();
    private TextureRect _mark = null!;
    private Label _name = null!;
    private Label _explanation = null!;
    private Label _word = null!;

    public override void _Ready()
    {
        _mark = GetNode<TextureRect>("%Mark");
        _name = GetNode<Label>("%Name");
        _explanation = GetNode<Label>("%Explanation");
        _word = GetNode<Label>("%Word");
    }

    public void Show(CheckRow row, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(palette);
        var (token, shape) = row.Status switch
        {
            CheckStatus.Passed => ("ok", HudShape.Done),
            CheckStatus.Failed => ("critical", HudShape.Failed),
            _ => ("unknown", HudShape.Unknown),
        };
        var failed = row.Status == CheckStatus.Failed;
        _variations.Set(this, failed ? ThemeVariationNames.BadgePanel("critical") : ThemeVariationNames.Badge);
        SelfModulate = failed ? Colors.White : Colors.Transparent;
        HudIcons.Show(_mark, shape, palette.Of(token));
        _name.Text = row.Title ?? row.Code;
        _explanation.ShowText(row.Explanation);
        _word.Text = row.Word;
        _word.SetFontColour(palette.Of(token));
    }
}
