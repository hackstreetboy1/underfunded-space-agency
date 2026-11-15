#if TOOLS
using Godot;
using Usa.Game.Presentation.Rendering;
using Usa.Game.Presentation.Spacecraft;

namespace Usa.Game.Editor;

/// <summary>
/// Import script for every asset .glb (MODELING 2, "Godot import"). Select the .glb, then in the Import
/// dock set Import Script to this file and press Reimport. It:
/// binds the shared USA_Palette / USA_Glass materials to the surfaces exported with those names;
/// copies the glTF extras <c>component_id</c>, <c>damage_variant</c>, <c>damage_conditions</c>,
/// <c>variant_mode</c> and <c>socket_for</c> (plume sockets, MODELING 5.4) into node metadata
/// (existing metadata is never overwritten); records which surfaces are palette surfaces and which material
/// goes in their override slot while unknown; hides damage variants; turns off shadow casting on tiny
/// meshes; logs a summary.
/// </summary>
[Tool]
public sealed partial class ModelImportPostprocess : EditorScenePostImport
{
    /// <summary>Meshes whose largest extent in the imported scene is below this cast no shadows.</summary>
    public const float TinyMeshThresholdM = 0.08f;

    private const string ExtrasMeta = "extras";

    private static readonly string[] CopiedExtras =
    [
        RenderingConventions.ComponentIdMeta,
        RenderingConventions.DamageVariantMeta,
        RenderingConventions.DamageConditionsMeta,
        RenderingConventions.VariantModeMeta,
        SpacecraftConventions.SocketForMeta,
    ];

    public override GodotObject _PostImport(Node scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var pass = new ImportPass(
            ResourceLoader.Load<Material>(RenderingConventions.PaletteMaterialPath),
            ResourceLoader.Load<Material>(RenderingConventions.GlassMaterialPath));
        if (pass.Palette is null) GD.PushError($"[ModelImport] {RenderingConventions.PaletteMaterialPath} missing; palette not bound.");

        Visit(scene, Transform3D.Identity, pass);

        GD.Print($"[ModelImport] {GetSourceFile()}: {pass.Meshes} meshes, {pass.PaletteSurfaces} palette + "
            + $"{pass.GlassSurfaces} glass surfaces bound, {pass.ComponentIds.Count} components, "
            + $"{pass.Variants} damage variants, {pass.Sockets} plume sockets, {pass.ShadowsOff} tiny meshes without shadows.");
        foreach (var warning in pass.Warnings) GD.PushWarning($"[ModelImport] {GetSourceFile()}: {warning}");
        return scene;
    }

    private static void Visit(Node node, Transform3D parentTransform, ImportPass pass)
    {
        var transform = node is Node3D node3D ? parentTransform * node3D.Transform : parentTransform;
        CopyExtras(node);
        if (node.HasMeta(SpacecraftConventions.SocketForMeta)) pass.Sockets++;

        if (node.HasMeta(RenderingConventions.ComponentIdMeta))
        {
            var id = node.GetMeta(RenderingConventions.ComponentIdMeta).AsString();
            if (!pass.ComponentIds.Add(id)) pass.Warnings.Add($"component_id '{id}' appears more than once ({node.Name}).");
            node.SetMeta(RenderingConventions.UnknownOverrideMeta, RenderingConventions.UnknownMaterialPath);
        }

        if (node is Node3D variant && node.HasMeta(RenderingConventions.DamageVariantMeta))
        {
            variant.Visible = false;
            pass.Variants++;
        }

        if (node is MeshInstance3D mesh) BindMesh(mesh, transform, pass);

        foreach (var child in node.GetChildren()) Visit(child, transform, pass);
    }

    // Blender writes object custom properties into the glTF node extras, which Godot stores as an
    // "extras" dictionary in the node metadata (and mesh-data properties on the mesh resource).
    private static void CopyExtras(Node node)
    {
        var sources = new List<GodotObject> { node };
        if (node is MeshInstance3D { Mesh: { } mesh }) sources.Add(mesh);
        foreach (var source in sources)
        {
            if (!source.HasMeta(ExtrasMeta)) continue;
            var extras = source.GetMeta(ExtrasMeta).AsGodotDictionary();
            foreach (var key in CopiedExtras)
            {
                if (extras.TryGetValue(key, out var value) && !node.HasMeta(key)) node.SetMeta(key, value.AsString());
            }
        }
    }

    private static void BindMesh(MeshInstance3D instance, Transform3D transform, ImportPass pass)
    {
        if (instance.Mesh is not { } mesh) return;
        pass.Meshes++;

        var paletteSurfaces = new List<int>();
        for (var i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            var name = mesh.SurfaceGetMaterial(i)?.ResourceName ?? string.Empty;
            if (name == RenderingConventions.PaletteMaterialName)
            {
                paletteSurfaces.Add(i);
                pass.PaletteSurfaces++;
                SetSurfaceMaterial(instance, mesh, i, pass.Palette, pass);
            }
            else if (name == RenderingConventions.GlassMaterialName)
            {
                pass.GlassSurfaces++;
                SetSurfaceMaterial(instance, mesh, i, pass.Glass, pass);
            }
        }
        if (paletteSurfaces.Count > 0)
        {
            instance.SetMeta(RenderingConventions.PaletteSurfacesMeta, Variant.CreateFrom(paletteSurfaces.ToArray()));
        }

        var size = (transform * mesh.GetAabb()).Size;
        if (Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) < TinyMeshThresholdM)
        {
            instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            pass.ShadowsOff++;
        }
    }

    // The base material goes on the mesh itself: the instance's override slot is reserved for the
    // knowledge look (ComponentPresenter).
    private static void SetSurfaceMaterial(MeshInstance3D instance, Mesh mesh, int surface, Material? material, ImportPass pass)
    {
        if (material is null) return;
        if (mesh is ArrayMesh arrayMesh)
        {
            arrayMesh.SurfaceSetMaterial(surface, material);
        }
        else
        {
            pass.Warnings.Add($"{instance.Name}: mesh is {mesh.GetClass()}, not ArrayMesh; surface {surface} left unbound.");
        }
    }

    private sealed class ImportPass(Material? palette, Material? glass)
    {
        public Material? Palette { get; } = palette;
        public Material? Glass { get; } = glass;
        public HashSet<string> ComponentIds { get; } = new(StringComparer.Ordinal);
        public List<string> Warnings { get; } = [];
        public int Meshes { get; set; }
        public int PaletteSurfaces { get; set; }
        public int GlassSurfaces { get; set; }
        public int Variants { get; set; }
        public int Sockets { get; set; }
        public int ShadowsOff { get; set; }
    }
}
#endif
