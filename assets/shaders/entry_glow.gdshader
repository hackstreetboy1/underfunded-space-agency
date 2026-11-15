// VFX-02b entry glow (MODELING 11): a Fresnel rim on a shell slightly larger than the heat shield, orange at low
// heating and white-hot at full. The same shader draws the faint wake behind the capsule (trail = true).
// The view feeds heat (0..1, ground-estimate heating) and calm (1 freezes the flicker for reduced motion / freeze).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform vec4 cool_color : source_color = vec4(1.0, 0.45, 0.2, 1.0);
uniform vec4 hot_color : source_color = vec4(1.0, 0.92, 0.8, 1.0);
uniform float heat : hint_range(0.0, 1.0) = 0.0;
uniform float energy : hint_range(0.0, 6.0) = 2.0;
uniform float rim_power : hint_range(0.5, 8.0) = 2.2;
uniform float flicker_hz : hint_range(0.0, 30.0) = 11.0;
uniform float flicker_amount : hint_range(0.0, 0.5) = 0.12;
// 1 = steady (reduced motion, frozen).
uniform float calm : hint_range(0.0, 1.0) = 0.0;
uniform bool trail = false;
// Trail cone: CylinderMesh height. The wide base (-y) sits at the capsule's afterbody, the tip (+y) trails behind the
// nose (the capsule flies heat shield first); the wake is brightest at the base and fades towards the tip.
uniform float trail_height_m = 3.0;

varying float along;

void vertex() {
    along = trail ? clamp(0.5 + VERTEX.y / trail_height_m, 0.0, 1.0) : 0.0;
}

void fragment() {
    float facing = clamp(abs(dot(normalize(NORMAL), VIEW)), 0.0, 1.0);
    float flick = 1.0 - flicker_amount * (1.0 - calm) * (0.5 + 0.5 * sin(TIME * flicker_hz));
    vec3 tint = mix(cool_color.rgb, hot_color.rgb, smoothstep(0.35, 1.0, heat));
    float shape = trail ? pow(facing, 1.5) * pow(1.0 - along, 2.0) * 0.35 : pow(1.0 - facing, rim_power) * smoothstep(0.0, 0.05, facing);
    ALBEDO = tint * shape * heat * energy * flick;
}
