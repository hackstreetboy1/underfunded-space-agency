using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>Reads the custom properties a model node carries, from a placeholder scene or an imported .glb alike.</summary>
public static class ModelProperties
{
    /// <summary>
    /// Node metadata (placeholders, and the keys the import step copies), else the raw glTF <c>extras</c> dictionary Godot
    /// keeps for Blender custom properties, so a .glb dropped in without a copy step (<c>chute_for</c>, <c>section_id</c>) works.
    /// </summary>
    public static bool TryGetString(Node node, string key, out string value)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.HasMeta(key))
        {
            value = node.GetMeta(key).AsString();
            return true;
        }
        if (node.HasMeta(SpacecraftConventions.GltfExtrasMeta) &&
            node.GetMeta(SpacecraftConventions.GltfExtrasMeta).AsGodotDictionary().TryGetValue(key, out var extra))
        {
            value = extra.AsString();
            return true;
        }
        value = string.Empty;
        return false;
    }
}
