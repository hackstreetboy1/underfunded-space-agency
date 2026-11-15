// ENV-01 atmosphere shell (MODELING 10.1): soft #7FB6E8 fresnel rim on a sphere slightly larger
// than the Earth. Additive and unlit; the night side keeps only a faint rim.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

uniform vec4 rim_color : source_color = vec4(0.498, 0.714, 0.910, 1.0);
uniform float rim_power : hint_range(0.5, 8.0) = 3.0;
uniform float rim_energy : hint_range(0.0, 4.0) = 1.2;
// Faint haze over the whole disc.
uniform float disc_haze : hint_range(0.0, 0.5) = 0.04;
// Fade at the very silhouette so the shell's edge never reads as a hard line.
uniform float edge_fade : hint_range(0.0, 0.5) = 0.08;
// World-space direction towards the Sun.
uniform vec3 sun_direction = vec3(1.0, 0.3, 0.0);
uniform float night_level : hint_range(0.0, 1.0) = 0.12;
uniform float terminator_softness : hint_range(0.01, 1.0) = 0.35;

varying vec3 world_normal;

void vertex() {
    world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

void fragment() {
    float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float rim = pow(1.0 - facing, rim_power) * smoothstep(0.0, edge_fade, facing);
    float day = smoothstep(-terminator_softness, terminator_softness,
            dot(normalize(world_normal), normalize(sun_direction)));
    ALBEDO = rim_color.rgb * (rim + disc_haze) * rim_energy * mix(night_level, 1.0, day);
}
