// VFX-03 reveal (MODELING 11, GDD 4.4): grey dissolves to colour outward from a world-space point
// with a thin #FFD64D edge line; reduced_motion turns it into a plain crossfade. RevealController
// duplicates reveal_transition.tres per reveal and tweens reveal_progress 0 -> 1 (0.6 s, or 0.2 s
// with reduced motion). Both ends match component_unknown / usa_palette_surface exactly, so
// swapping materials before and after the transition does not pop.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://assets/shaders/usa_palette_common.gdshaderinc"

uniform vec3 reveal_origin = vec3(0.0);
// Furthest distance from reveal_origin to the component's surface; set by RevealController.
uniform float reveal_radius_m : hint_range(0.0, 20.0) = 1.0;
uniform float reveal_progress : hint_range(0.0, 1.0) = 0.0;
uniform float edge_width_m : hint_range(0.0, 0.2) = 0.02;
uniform vec4 edge_color : source_color = vec4(1.0, 0.839, 0.302, 1.0);
uniform float edge_energy : hint_range(0.0, 16.0) = 3.0;
// Irregularity of the front so it reads as a dissolve rather than a perfect sphere.
uniform float breakup_m : hint_range(0.0, 0.5) = 0.04;
uniform float breakup_scale : hint_range(0.1, 50.0) = 9.0;
uniform bool reduced_motion = false;

varying vec3 world_pos;

float hash13(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

float value_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = hash13(i);
    float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash13(i + vec3(1.0, 1.0, 1.0));
    return mix(
            mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
            mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y),
            f.z);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    UsaSurface unknown = usa_unknown_surface(UV);
    UsaSurface known = usa_palette_surface(UV, indicator_level);

    float revealed = 0.0;
    float line = 0.0;
    if (reduced_motion) {
        revealed = reveal_progress;
    } else {
        float dist = length(world_pos - reveal_origin) + value_noise(world_pos * breakup_scale) * breakup_m;
        // Starts fully grey (front behind the origin) and ends with the edge line past the far side.
        float radius = mix(-edge_width_m, reveal_radius_m + breakup_m + 2.0 * edge_width_m, reveal_progress);
        float front = radius - dist;
        float aa = max(fwidth(front), 0.0001);
        revealed = smoothstep(-aa, aa, front);
        line = revealed * (1.0 - smoothstep(edge_width_m - aa, edge_width_m + aa, front));
        // Exact end state even where fwidth is large (grazing angles, far away).
        float done = step(1.0, reveal_progress);
        revealed = max(revealed, done);
        line *= 1.0 - done;
    }

    UsaSurface s = usa_mix_surface(unknown, known, revealed);
    ALBEDO = mix(s.albedo, edge_color.rgb, line);
    ROUGHNESS = s.roughness;
    METALLIC = mix(s.metallic, 0.0, line);
    SPECULAR = s.specular;
    EMISSION = s.emission + edge_color.rgb * edge_energy * line;
}
