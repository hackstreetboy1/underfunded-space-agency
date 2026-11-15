// USA_Palette: the confirmed (nominal) look of every non-crew asset (MODELING 1.3).
// Bound to palette surfaces by src/Game/Editor/ModelImportPostprocess.cs; material:
// res://assets/materials/usa_palette.tres.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://assets/shaders/usa_palette_common.gdshaderinc"

void fragment() {
    UsaSurface s = usa_palette_surface(UV, indicator_level);
    ALBEDO = s.albedo;
    ROUGHNESS = s.roughness;
    METALLIC = s.metallic;
    SPECULAR = s.specular;
    EMISSION = s.emission;
}
