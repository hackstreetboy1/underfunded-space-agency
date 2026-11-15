using Godot;
using Usa.Game.Content;
using Usa.Game.Flow;
using Usa.Game.UI.Frontend;

namespace Usa.Game.Diagnostics.Crash;

/// <summary>
/// The player's side of a crash (every build): a small, calm card in the bottom-right corner saying that something went
/// wrong, where the report was saved, and that play can go on. "Keep playing" closes it; during a mission "Return to menu"
/// opens the Esc menu's abandon confirmation (the run then ends without a grade), so nothing is lost on one click. It never
/// takes focus, pauses or blocks the rest of the screen, so an open dialog keeps the keyboard. Words: <c>escMenu.crashNotice*</c>
/// in <c>strings.json</c>, with the same copy built in for when the strings cannot be read. Call <see cref="Initialize"/>
/// before adding it to the tree.
/// </summary>
public sealed partial class CrashNotice : CanvasLayer
{
    /// <summary>Above the menus and overlays (Main.tscn layers 20-60), below the F12 overlay (120).</summary>
    public const int CanvasLayerIndex = 110;

    private const int MarginPx = 24;
    private const int WidthPx = 400;

    private CrashReporter _crash = null!;
    private ScreenRouter _router = null!;
    private ITextSource? _text;
    private Control _card = null!;
    private Label _body = null!;
    private Label _menuHint = null!;
    private Button _returnToMenu = null!;

    /// <param name="text">The strings table; null uses the built-in copy.</param>
    public void Initialize(CrashReporter crash, ScreenRouter router, ITextSource? text)
    {
        _crash = crash ?? throw new ArgumentNullException(nameof(crash));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _text = text;
    }

    public override void _Ready()
    {
        if (_crash is null) throw new InvalidOperationException($"{nameof(CrashNotice)}.{nameof(Initialize)} must be called before adding it to the tree.");
        Layer = CanvasLayerIndex;
        ProcessMode = ProcessModeEnum.Always;

        var column = Ui.Column(8);
        column.CustomMinimumSize = new Vector2(WidthPx, 0);
        column.AddChild(Ui.Label(Word("escMenu.crashNoticeTitle", "Something went wrong"), Ui.H2));
        _body = Ui.Label("", Ui.Small, wrap: true);
        column.AddChild(_body);
        _menuHint = Ui.Label(Word("escMenu.crashNoticeMenuHint", "Or return to the menu: this run then ends without a grade."), Ui.Small, wrap: true);
        column.AddChild(_menuHint);

        var buttons = Ui.Row(8);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        _returnToMenu = Ui.Button(Word("escMenu.crashNoticeReturnToMenu", "Return to menu"), Ui.ButtonGhost, ReturnToMenu);
        buttons.AddChild(_returnToMenu);
        buttons.AddChild(Ui.Button(Word("escMenu.crashNoticeKeepPlaying", "Keep playing"), Ui.ButtonGhost, Close));
        column.AddChild(buttons);

        _card = Ui.Panel(column, 16, Ui.DialogPanel);
        _card.MouseFilter = Control.MouseFilterEnum.Stop;
        _card.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _card.GrowHorizontal = Control.GrowDirection.Begin;
        _card.GrowVertical = Control.GrowDirection.Begin;
        _card.OffsetLeft = _card.OffsetRight = -MarginPx;
        _card.OffsetTop = _card.OffsetBottom = -MarginPx;
        AddChild(_card);

        Visible = false;
        _crash.Reported += OnReported;
        _router.Changed += UpdateMenuChoice;
    }

    public override void _ExitTree()
    {
        _crash.Reported -= OnReported;
        _router.Changed -= UpdateMenuChoice;
    }

    private void OnReported(CrashReport report)
    {
        _body.Text = report.ReproPath is { } path
            ? Format("escMenu.crashNoticeSaved", "A report was saved to {path}. You can keep playing.", path)
            : Word("escMenu.crashNoticeNotSaved", "A report could not be saved; the game log has the details. You can keep playing.");
        Visible = true;
        UpdateMenuChoice();
    }

    // Only a running mission has a menu to return to; while a console dialog or the connection overlay holds the screen,
    // the Esc menu cannot open, so the choice waits.
    private void UpdateMenuChoice()
    {
        if (!Visible) return;
        var inMission = _router.Current == Screen.Mission && _router.MissionInProgress;
        _returnToMenu.Visible = _menuHint.Visible = inMission;
        _returnToMenu.Disabled = _router.MissionDialogOpen || _router.ConnectionOverlayVisible
            || _router.EscMenu is not (EscMenuState.Closed or EscMenuState.Open);
    }

    private void ReturnToMenu()
    {
        if (_router.EscMenu == EscMenuState.Closed && !_router.OpenEscMenu()) return;
        if (_router.RequestAbandon()) Close();
    }

    private void Close() => Visible = false;

    private string Word(string key, string fallback)
    {
        if (_text is null) return fallback;
        try
        {
            var value = _text.Get(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch (KeyNotFoundException)
        {
            return fallback;
        }
    }

    private string Format(string key, string fallback, string path) =>
        TextTemplate.Fill(Word(key, fallback), new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = path });
}
