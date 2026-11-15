using Godot;
using Usa.Game.Presentation.Rendering;

namespace Usa.Game.Presentation.Spacecraft.Placeholders;

/// <summary>
/// Root script of the hand-built placeholder vehicles (<c>res://scenes/placeholders</c>), used until the Gate 3 models
/// are approved. Primitive meshes have UVs spread over the whole texture, but palette materials read one swatch per
/// face (MODELING 1.3). On ready, every MeshInstance3D with <c>placeholder_swatch</c> metadata gets an ArrayMesh copy
/// of its primitive whose UVs all sit on that swatch's centre, the shared <c>USA_Palette</c> material and the
/// <c>usa_palette_surfaces</c> metadata the importer would write, so <see cref="ComponentPresenter"/> treats it exactly
/// like an imported part. Runs before the parent view binds presenters (children are ready first).
/// Deleted together with the placeholders.
/// </summary>
public sealed partial class PlaceholderPaletteMeshes : Node3D
{
    public const string SwatchMeta = "placeholder_swatch";

    private const int PaletteGrid = 8;

    private static readonly int[] FirstSurface = [0];

    public override void _Ready()
    {
        var palette = GD.Load<Material>(RenderingConventions.PaletteMaterialPath);
        var converted = new Dictionary<(Mesh, int), ArrayMesh>();
        Convert(this, palette, converted);
    }

    /// <summary>UV of a swatch's centre as Godot sees the palette (row 7 − i / 8 from the top, column i % 8).</summary>
    public static Vector2 SwatchUv(int swatch)
    {
        var column = swatch % PaletteGrid;
        var row = PaletteGrid - 1 - swatch / PaletteGrid;
        return new Vector2((column + 0.5f) / PaletteGrid, (row + 0.5f) / PaletteGrid);
    }

    private static void Convert(Node node, Material palette, Dictionary<(Mesh, int), ArrayMesh> converted)
    {
        if (node is MeshInstance3D { Mesh: PrimitiveMesh primitive } instance && node.HasMeta(SwatchMeta))
        {
            var swatch = node.GetMeta(SwatchMeta).AsInt32();
            if (!converted.TryGetValue((primitive, swatch), out var mesh))
            {
                mesh = ToPaletteMesh(primitive, swatch, palette);
                converted.Add((primitive, swatch), mesh);
            }
            instance.Mesh = mesh;
            instance.SetMeta(RenderingConventions.PaletteSurfacesMeta, FirstSurface);
        }
        foreach (var child in node.GetChildren()) Convert(child, palette, converted);
    }

    private static ArrayMesh ToPaletteMesh(PrimitiveMesh primitive, int swatch, Material palette)
    {
        var arrays = primitive.GetMeshArrays();
        var vertexCount = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
        var uvs = new Vector2[vertexCount];
        Array.Fill(uvs, SwatchUv(swatch));
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, palette);
        return mesh;
    }
}
