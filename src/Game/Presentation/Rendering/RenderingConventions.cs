namespace Usa.Game.Presentation.Rendering;

/// <summary>Metadata keys and resource paths shared by the model importer and the presenters (MODELING 2).</summary>
public static class RenderingConventions
{
    /// <summary>Vehicle-JSON id on a revealable component node (from the glTF extras).</summary>
    public const string ComponentIdMeta = "component_id";

    /// <summary>Damage-variant name on a variant node, e.g. <c>stuck_scorched</c>.</summary>
    public const string DamageVariantMeta = "damage_variant";

    /// <summary>
    /// Comma-separated knowledge variant words a variant mesh depicts, e.g. <c>stuck_open</c> on the
    /// <c>stuck_scorched</c> mesh (the simulation reports <c>failed:stuck_open</c>).
    /// </summary>
    public const string DamageConditionsMeta = "damage_conditions";

    /// <summary><c>overlay</c> (default: drawn on top of the base mesh) or <c>replace</c> (base hidden).</summary>
    public const string VariantModeMeta = "variant_mode";

    /// <summary>Surface indices of a MeshInstance3D that use the shared palette material.</summary>
    public const string PaletteSurfacesMeta = "usa_palette_surfaces";

    /// <summary>Material a presenter puts in the override slot of palette surfaces while unknown.</summary>
    public const string UnknownOverrideMeta = "usa_unknown_override";

    public const string ReplaceVariantMode = "replace";

    public const string PaletteMaterialName = "USA_Palette";
    public const string GlassMaterialName = "USA_Glass";

    public const string PaletteMaterialPath = "res://assets/materials/usa_palette.tres";
    public const string GlassMaterialPath = "res://assets/materials/usa_glass.tres";
    public const string UnknownMaterialPath = "res://assets/materials/component_unknown.tres";
    public const string RevealMaterialPath = "res://assets/materials/reveal_transition.tres";
}
