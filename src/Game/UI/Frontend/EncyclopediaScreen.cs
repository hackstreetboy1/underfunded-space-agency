using Godot;
using Usa.Game.Flow.Screens;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The full-screen encyclopedia reached from the main menu (<c>Encyclopedia.tscn</c>, UI.md 5): the G6-07 sheet content on a
/// page with a back link. The "Ask the engineers" box is hidden here, since the engineers answer through a running mission.
/// </summary>
public sealed partial class EncyclopediaScreen : Control, IFrontendScreen
{
    private Button _back = null!;
    private Control _sheetHost = null!;

    public override void _Ready()
    {
        _back = GetNode<Button>("%Back");
        _sheetHost = GetNode<Control>("%SheetHost");
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _back.Text = context.Text.Get("mainMenu.back");
        Ui.SetIcon(_back, Ui.IconBack, context.Tokens.ColorTextMid);
        _back.Pressed += () => context.Router.Back();
        if (context.Encyclopedia is not { } encyclopedia) return;

        var scene = ResourceLoader.Load<PackedScene>(Model.FrontendRoutes.EncyclopediaSheet);
        if (scene?.Instantiate() is not EncyclopediaSheetView sheet) return;
        _sheetHost.AddChild(sheet);
        sheet.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        sheet.Initialize(context, new EncyclopediaSheetModel(encyclopedia, context.Strings), onClose: null, inMission: false);
        sheet.Open();
    }
}
