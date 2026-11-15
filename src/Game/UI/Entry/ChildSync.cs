using Godot;

namespace Usa.Game.UI.Entry;

/// <summary>Keeps a container's instanced rows in step with a model list (the HUD, recovery card and workspace lists).</summary>
internal static class ChildSync
{
    /// <summary>Adds or frees instances of <paramref name="scene"/> until <paramref name="parent"/> has <paramref name="count"/> children.</summary>
    public static void Sync(Node parent, int count, PackedScene? scene)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(scene);
        while (parent.GetChildCount() > count) parent.GetChild(parent.GetChildCount() - 1).Free();
        while (parent.GetChildCount() < count) parent.AddChild(scene.Instantiate());
    }
}
