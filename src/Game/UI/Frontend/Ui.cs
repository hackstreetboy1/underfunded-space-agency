using Godot;
using Usa.Game.UI.Frontend.Model;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Builders for the rows the front-end scenes repeat. Every style is a theme type variation of <c>UsaTheme.tres</c>
/// (Title, Grade, GradeSmall, H1, H2, Display, Small, Meta, TapeLabel, Badge*, Button*); the few colours drawn in code
/// come from the active <see cref="UiTokenSet"/>, never from literals.
/// </summary>
public static class Ui
{
    public const string Title = "Title";
    public const string Grade = "Grade";
    public const string GradeSmall = "GradeSmall";
    public const string Display = "Display";
    public const string H1 = "H1";
    public const string H2 = "H2";
    public const string Small = "Small";
    public const string Meta = "Meta";
    public const string Tape = "TapeLabel";
    public const string Readout = "Readout";
    public const string ReadoutSmall = "ReadoutSmall";
    public const string MetClock = "MetClock";
    public const string Caption = "Caption";
    public const string ButtonPrimary = "ButtonPrimary";
    public const string ButtonGhost = "ButtonGhost";
    public const string ButtonDanger = "ButtonDanger";
    public const string DialogPanel = "DialogPanel";
    public const string PanelHeader = "PanelHeader";
    public const string Scrim = "Scrim";

    public static readonly string IconLocked = UiIcons.PathOf(UiIcons.StateLocked);
    public static readonly string IconBack = UiIcons.PathOf(UiIcons.ActionBack);
    public static readonly string IconClose = UiIcons.PathOf(UiIcons.ActionClose);
    public static readonly string IconCheck = UiIcons.PathOf(UiIcons.ActionCheck);
    public static readonly string IconImage = UiIcons.PathOf(UiIcons.ContentImage);
    public static readonly string IconEncyclopedia = UiIcons.PathOf(UiIcons.ActionEncyclopedia);

    /// <summary>A UI.md 8.1 icon (white SVG) tinted with a token colour; empty when the icon is not imported.</summary>
    public static TextureRect Icon(string path, Color tint, int sizePx = 16) => new()
    {
        Texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(sizePx, sizePx),
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        SelfModulate = tint,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Puts an icon on a button, tinted like its label.</summary>
    public static void SetIcon(Button button, string path, Color tint)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (!ResourceLoader.Exists(path)) return;
        button.Icon = ResourceLoader.Load<Texture2D>(path);
        button.ExpandIcon = false;
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color", "icon_hover_pressed_color" })
            button.AddThemeColorOverride(state, tint);
    }

    /// <summary>A row of an icon and a label.</summary>
    public static HBoxContainer IconLabel(string iconPath, Color tint, string text, string variation = "")
    {
        var row = Row(8);
        row.AddChild(Icon(iconPath, tint));
        row.AddChild(Label(text, variation));
        return row;
    }

    public static Label Label(string? text, string variation = "", bool wrap = false)
    {
        var label = new Label { Text = text ?? string.Empty, ThemeTypeVariation = variation };
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.CustomMinimumSize = new Vector2(1, 0);
        }
        return label;
    }

    public static Button Button(string text, string variation = "", Action? pressed = null)
    {
        var button = new Button { Text = text, ThemeTypeVariation = variation, FocusMode = Control.FocusModeEnum.All };
        if (pressed is not null) button.Pressed += pressed;
        return button;
    }

    public static VBoxContainer Column(int separation)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    public static HBoxContainer Row(int separation)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>A bordered panel (theme <c>PanelContainer</c>, or a variation) with inner padding.</summary>
    public static PanelContainer Panel(Control content, int padding, string variation = "")
    {
        var panel = new PanelContainer { ThemeTypeVariation = variation };
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" }) margin.AddThemeConstantOverride(side, padding);
        margin.AddChild(content);
        panel.AddChild(margin);
        return panel;
    }

    /// <summary>A masking-tape label (UI.md 1.5): the theme's tape style, shrunk to its text.</summary>
    public static Label TapeLabel(string text) =>
        new() { Text = text, ThemeTypeVariation = Tape, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };

    /// <summary>A badge ("MODERN REFERENCE", "LOCKED") in a tone: <c>Badge*</c> panel plus <c>BadgeLabel*</c> word.</summary>
    public static PanelContainer Badge(string text, string tone = "")
    {
        var panel = new PanelContainer { ThemeTypeVariation = "Badge" + tone, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        panel.AddChild(new Label { Text = text.ToUpperInvariant(), ThemeTypeVariation = "BadgeLabel" + tone });
        return panel;
    }

    /// <summary>A key cap ("Space", "Ctrl+F") in the small mono readout style on a neutral badge.</summary>
    public static PanelContainer KeyCap(string text)
    {
        var panel = new PanelContainer { ThemeTypeVariation = "Badge", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        panel.AddChild(new Label { Text = text, ThemeTypeVariation = ReadoutSmall });
        return panel;
    }

    /// <summary>A 1 px divider in <c>line.1</c>.</summary>
    public static ColorRect Divider(UiTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new ColorRect { Color = tokens.ColorLine1, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };
    }

    public static Control Spacer(bool vertical = true)
    {
        var spacer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (vertical) spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        else spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return spacer;
    }

    public static Color ToneColour(DebriefTone tone, UiTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return tone switch
        {
            DebriefTone.Ok => tokens.ColorOk,
            DebriefTone.Caution => tokens.ColorCaution,
            DebriefTone.Critical => tokens.ColorCritical,
            _ => tokens.ColorTextMid,
        };
    }

    /// <summary>A thin flat bar (score dimensions, loading progress): track in <c>bg.3</c>, fill in the given colour.</summary>
    public static ProgressBar Bar(double ratio, Color fill, UiTokenSet tokens, int heightPx)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.001,
            Value = Math.Clamp(ratio, 0, 1),
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, heightPx),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = tokens.ColorBg3 });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        return bar;
    }

    /// <summary>Frees every child now (they are removed from the tree at once, freed at the end of the frame).</summary>
    public static void Clear(Node parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>A cached NASA image by its manifest path (relative to <c>res://</c>); null when not imported.</summary>
    public static Texture2D? LoadImage(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath)) return null;
        var path = localPath.StartsWith("res://", StringComparison.Ordinal) ? localPath : "res://" + localPath;
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
    }

    /// <summary>Full-rect anchors for a child that fills its parent.</summary>
    public static T Fill<T>(T control) where T : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return control;
    }
}
