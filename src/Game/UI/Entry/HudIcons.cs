using Godot;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Entry;

/// <summary>
/// The status-language shapes (UI.md 2.2) as the project's own SVG icons (<c>res://assets/ui/icons</c>, tinted at runtime),
/// so no status depends on font fallback: IBM Plex has none of the status shapes (review 3, R3-11). The map, entry HUD
/// and workspace use this one set; every shape is still paired with a word.
/// </summary>
public static class HudIcons
{
    private static readonly Lazy<Texture2D>[] Textures = Enum.GetValues<HudShape>()
        .Select(shape => new Lazy<Texture2D>(() => ResourceLoader.Load<Texture2D>(PathOf(shape))))
        .ToArray();

    /// <summary>The approved icon (<see cref="UiIcons"/>, glyph map R3-11) for a shape.</summary>
    public static string IdOf(HudShape shape) => shape switch
    {
        HudShape.Nominal => UiIcons.StatusNominal,
        HudShape.Caution => UiIcons.StatusCaution,
        HudShape.Failed => UiIcons.StatusFailed,
        HudShape.Isolated => UiIcons.StatusIsolated,
        HudShape.Suspect => UiIcons.StatusSuspect,
        HudShape.Stale => UiIcons.StateStale,
        HudShape.Done => UiIcons.ActionCheck,
        HudShape.Invalid => UiIcons.ActionClose,
        _ => UiIcons.StatusUnknown,
    };

    public static string PathOf(HudShape shape) => UiIcons.PathOf(IdOf(shape));

    /// <summary>Any approved icon by id, e.g. <c>UiIcons.PortRound</c>.</summary>
    public static Texture2D Texture(string iconId) => ResourceLoader.Load<Texture2D>(UiIcons.PathOf(iconId));

    /// <summary>The imported texture, loaded once per shape.</summary>
    public static Texture2D Texture(HudShape shape) => Textures[(int)shape].Value;

    /// <summary>Shows <paramref name="shape"/> in <paramref name="icon"/> tinted with <paramref name="tint"/>.</summary>
    public static void Show(TextureRect icon, HudShape shape, Color tint)
    {
        ArgumentNullException.ThrowIfNull(icon);
        var texture = Texture(shape);
        if (!ReferenceEquals(icon.Texture, texture)) icon.Texture = texture;
        icon.SelfModulate = tint;
    }

    /// <summary>A 14 px icon for rows built in code (planner warnings, test summary).</summary>
    public static TextureRect Create(int sizePx = 14) => new()
    {
        CustomMinimumSize = new Vector2(sizePx, sizePx),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
}
