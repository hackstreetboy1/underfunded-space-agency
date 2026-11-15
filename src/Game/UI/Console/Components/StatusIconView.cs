using Godot;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A 16 px status shape (UI.md 2.2): the approved icon tinted with the status colour, or, for the hollow circle that has no
/// icon, a drawn ring. Hidden for <see cref="StatusShape.None"/>. Godot's resource cache shares each texture.
/// </summary>
public sealed partial class StatusIconView : Control
{
    private Texture2D? _texture;
    private bool _ring;
    private Color _colour = Colors.White;

    public static StatusIconView Create(int sizePx = 16) => new()
    {
        CustomMinimumSize = new Vector2(sizePx, sizePx),
        SizeFlagsVertical = SizeFlags.ShrinkCenter,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    public void Show(StatusShape shape, Color colour)
    {
        _ring = shape == StatusShape.HollowCircle;
        _texture = StatusIcons.For(shape) is { } id ? Load(id) : null;
        _colour = colour;
        Visible = _ring || _texture is not null;
        QueueRedraw();
    }

    /// <summary>Any approved icon by id (trend arrows); null hides it.</summary>
    /// <summary>Puts an approved icon on a glyph-only button (✕, ▾); the button keeps its text when the icon is missing.</summary>
    public static void UseIcon(Button button, string iconId)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (Load(iconId) is not { } texture) return;
        button.Icon = texture;
        button.Text = string.Empty;
        button.ExpandIcon = false;
    }

    /// <summary>An approved icon as a texture (for buttons); null when the file is missing.</summary>
    public static Texture2D? Texture(string iconId) => Load(iconId);

    public void ShowIcon(string? iconId, Color colour)
    {
        _ring = false;
        _texture = iconId is null ? null : Load(iconId);
        _colour = colour;
        Visible = _texture is not null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var side = Math.Min(Size.X, Size.Y);
        var rect = new Rect2((Size - new Vector2(side, side)) / 2, new Vector2(side, side));
        if (_texture is { } texture) DrawTextureRect(texture, rect, tile: false, _colour);
        else if (_ring) DrawArc(Size / 2, side / 2 - 1.5f, 0, Mathf.Tau, 24, _colour, 1.5f, antialiased: true);
    }

    private static Texture2D? Load(string id)
    {
        var path = UiIcons.PathOf(id);
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
    }
}
