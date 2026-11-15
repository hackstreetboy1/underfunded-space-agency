using Godot;
using Usa.Game.Content;

namespace Usa.Game.UI.Entry;

/// <summary>One fixed label, button caption or tooltip of a scene and its <c>strings.json</c> key.</summary>
/// <param name="Path">Node path from the view root (<c>%Unique</c> or relative).</param>
internal readonly record struct TextSlot(string Path, string Key, bool Tooltip = false);

/// <summary>
/// Puts a scene's fixed words through <c>content/strings.json</c> once, when the view is given its text source (the
/// <c>ConsoleLabels</c> pattern): the scenes keep the same words as design text for previews that bind no strings.
/// </summary>
internal static class SceneText
{
    public static void Apply(Node root, ITextSource text, IReadOnlyList<TextSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(slots);
        foreach (var slot in slots)
        {
            var node = root.GetNode(slot.Path);
            var value = text.Get(slot.Key);
            switch (node)
            {
                case Control control when slot.Tooltip:
                    control.TooltipText = value;
                    break;
                case Label label:
                    label.Text = value;
                    break;
                case Button button:
                    button.Text = value;
                    break;
                case LineEdit edit:
                    edit.PlaceholderText = value;
                    break;
            }
        }
    }
}
