using Godot;
using Usa.Game.Flow.Screens;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Credits and the privacy-relevant tool credits (<c>Credits.tscn</c>, Figma G6-09; UI.md 5, GDD 16): the lockup, team, tools
/// (voices credited to ElevenLabs, language to Google, matching the privacy notice), IBM Plex under the OFL, one source credit
/// per shipped NASA item, the support line and "Not affiliated with or endorsed by NASA."
/// </summary>
public sealed partial class CreditsScreen : Control, IFrontendScreen
{
    private Button _back = null!;
    private Label _escHint = null!;
    private Label _mark = null!;
    private Label _agency = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private VBoxContainer _left = null!;
    private VBoxContainer _right = null!;
    private VBoxContainer _footer = null!;
    private FrontendContext? _context;

    public override void _Ready()
    {
        _back = GetNode<Button>("%Back");
        _escHint = GetNode<Label>("%EscHint");
        _mark = GetNode<Label>("%Mark");
        _agency = GetNode<Label>("%Agency");
        _title = GetNode<Label>("%Title");
        _subtitle = GetNode<Label>("%Subtitle");
        _left = GetNode<VBoxContainer>("%Left");
        _right = GetNode<VBoxContainer>("%Right");
        _footer = GetNode<VBoxContainer>("%Footer");
        _back.Pressed += () => _context?.Router.Back();
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        var text = context.Text;
        var model = new CreditsModel(context.Strings, context.Manifest);

        _back.Text = text.Get("mainMenu.back");
        Ui.SetIcon(_back, Ui.IconBack, context.Tokens.ColorTextMid);
        _escHint.Text = text.Get("credits.escHint");
        _mark.Text = text.Get("agency.headerMark");
        _agency.Text = text.Get("agency.officialName");
        _title.Text = text.Get("agency.gameTitle");
        _subtitle.Text = text.Get("mainMenu.subtitle");

        Ui.Clear(_left);
        Ui.Clear(_right);
        Ui.Clear(_footer);
        var sections = model.Sections;
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var last = i == sections.Count - 1;
            var target = last ? _footer : i == sections.Count - 2 ? _right : _left;
            if (!last) target.AddChild(Ui.Label(section.Title.ToUpperInvariant(), Ui.H2));
            for (var l = 0; l < section.Lines.Count; l++)
            {
                var line = section.Lines[l];
                var label = Ui.Label(line, last && l == section.Lines.Count - 1 ? Ui.H2 : last ? "" : l == 0 ? "" : Ui.Small, wrap: true);
                if (last) label.HorizontalAlignment = HorizontalAlignment.Center;
                target.AddChild(label);
            }
            if (!last) target.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12), MouseFilter = MouseFilterEnum.Ignore });
        }
        Callable.From(() => { if (IsInstanceValid(_back) && _back.IsInsideTree()) _back.GrabFocus(); }).CallDeferred();
    }
}
