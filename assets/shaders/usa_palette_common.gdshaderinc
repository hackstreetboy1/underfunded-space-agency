// Shared by usa_palette_surface, component_unknown and reveal_transition, so the confirmed look,
// the unknown look and both ends of the reveal are computed by the same code (MODELING 1.3, 1.4).
//
// Palette layout: 8x8 swatches of 16 px. Blender puts swatch i in row i / 8 from the bottom; after
// the glTF V flip Godot sees it in row 7 - i / 8 from the top (tools/blender/usa_palette.py).

uniform sampler2D palette_albedo : source_color, filter_nearest, repeat_disable;
// glTF layout: G = roughness, B = metallic (R unused).
uniform sampler2D palette_rm : hint_default_white, filter_nearest, repeat_disable;

// Indicator swatches ('light', #9) glow; the presenter can dim one instance with indicator_level.
// Read indicator_level in fragment() and pass it in: instance uniforms stay out of helper functions.
uniform bool indicator_emission = true;
uniform int emissive_swatch : hint_range(0, 63) = 9;
uniform float indicator_energy : hint_range(0.0, 16.0) = 2.5;
instance uniform float indicator_level : hint_range(0.0, 1.0) = 1.0;

// Unknown look (Appendix D): flat #9AA1AA-range grey that keeps a little of the swatch's value.
uniform vec4 unknown_grey : source_color = vec4(0.604, 0.631, 0.667, 1.0);
uniform float unknown_value_keep : hint_range(0.0, 1.0) = 0.3;
uniform float unknown_roughness : hint_range(0.0, 1.0) = 0.85;
uniform float unknown_specular : hint_range(0.0, 1.0) = 0.2;

const vec3 USA_LUMA = vec3(0.2126, 0.7152, 0.0722);

int usa_swatch_index(vec2 uv) {
    ivec2 cell = ivec2(floor(clamp(uv, vec2(0.0), vec2(0.99999)) * 8.0));
    return (7 - cell.y) * 8 + cell.x;
}

struct UsaSurface {
    vec3 albedo;
    float roughness;
    float metallic;
    float specular;
    vec3 emission;
};

UsaSurface usa_palette_surface(vec2 uv, float level) {
    vec3 albedo = texture(palette_albedo, uv).rgb;
    vec3 rm = texture(palette_rm, uv).rgb;
    vec3 emission = vec3(0.0);
    if (indicator_emission && usa_swatch_index(uv) == emissive_swatch) {
        emission = albedo * indicator_energy * level;
        // An unlit indicator must also look unlit in sunlight, not just stop glowing.
        albedo *= mix(0.3, 1.0, level);
    }
    return UsaSurface(albedo, rm.g, rm.b, 0.5, emission);
}

UsaSurface usa_unknown_surface(vec2 uv) {
    vec3 albedo = texture(palette_albedo, uv).rgb;
    float grey_luma = max(dot(unknown_grey.rgb, USA_LUMA), 0.0001);
    float shade = clamp(mix(1.0, dot(albedo, USA_LUMA) / grey_luma, unknown_value_keep), 0.7, 1.15);
    return UsaSurface(unknown_grey.rgb * shade, unknown_roughness, 0.0, unknown_specular, vec3(0.0));
}

UsaSurface usa_mix_surface(UsaSurface a, UsaSurface b, float t) {
    return UsaSurface(
            mix(a.albedo, b.albedo, t),
            mix(a.roughness, b.roughness, t),
            mix(a.metallic, b.metallic, t),
            mix(a.specular, b.specular, t),
            mix(a.emission, b.emission, t));
}
