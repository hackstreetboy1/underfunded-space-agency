using Godot;

namespace Usa.Game.Presentation.Rendering;

/// <summary>One surface of a mesh instance that is drawn with the shared palette material.</summary>
public readonly record struct PaletteSurface(MeshInstance3D Mesh, int Surface)
{
    public void SetOverride(Material? material) => Mesh.SetSurfaceOverrideMaterial(Surface, material);
}
