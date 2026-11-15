using Godot;
using Usa.Game.UI.Entry;
using Usa.Game.UI.Theme;

namespace Usa.Game.UI.Workspace;

/// <summary>
/// The G5-02 part and port icons (Figma node 30:2369, <see cref="UiIcons"/>), keyed like <c>PartCard.IconKey</c> and
/// <c>ConnectorChip.ShapeKey</c>; they replace the text stand-ins IBM Plex could not draw (R3-11).
/// </summary>
public static class PartIcons
{
    public static string PartId(string iconKey) =>
        UiIcons.TryGet("icon_part_" + iconKey, out var icon) ? icon.Id : UiIcons.PartSpare;

    /// <summary>D-type or round; unknown shapes fall back to the generic spare.</summary>
    public static string PortId(string shapeKey) =>
        UiIcons.TryGet("icon_port_" + shapeKey, out var icon) ? icon.Id : UiIcons.PartSpare;

    public static Texture2D Part(string iconKey) => HudIcons.Texture(PartId(iconKey));

    public static Texture2D Port(string shapeKey) => HudIcons.Texture(PortId(shapeKey));
}
