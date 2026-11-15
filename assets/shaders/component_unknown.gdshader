// Unknown-component override (MODELING 1.4, GDD 4.4): the palette colour desaturated and lifted to
// a flat #9AA1AA-range grey with reduced specular and no indicator glow. Applied per palette
// surface by ComponentPresenter as a surface override; material: component_unknown.tres.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://assets/shaders/usa_palette_common.gdshaderinc"

void fragment() {
    UsaSurface s = usa_unknown_surface(UV);
    ALBEDO = s.albedo;
    ROUGHNESS = s.roughness;
    METALLIC = s.metallic;
    SPECULAR = s.specular;
}
