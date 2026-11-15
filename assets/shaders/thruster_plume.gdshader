// VFX-01/02a thruster plumes (MODELING 11): a soft additive cone with a hotter core and flicker.
// Mesh: a CylinderMesh of height 1 and radius 0.5 (no caps); the vertex pass reshapes it into a
// cone that starts at the node origin and points along local +X, matching the Socket_<id>
// convention (MODELING 5.4). The cone is longer than the unit mesh, so give the instance
// extra_cull_margin >= length_m (or a custom_aabb).
// Per instance: throttle (0 hides it, pulses drive it 0 -> 1 -> 0) and seed (desyncs flicker).
//
// Brightness: both walls of the cone add up, and the view tonemaps (Filmic, exposure 1.2), so
// keep energy * (core peak) below ~0.5 per wall; brighter cones bloom and clip to flat white and
// the core tint (stuck T8 orange, engine blue) is lost. Values: docs/review/gate-3/lookdev/LOOKDEV.md.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform vec4 core_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 outer_color : source_color = vec4(0.80, 0.88, 1.0, 1.0);
uniform float energy : hint_range(0.0, 16.0) = 0.45;
uniform float length_m : hint_range(0.01, 20.0) = 0.35;
uniform float exit_radius_m : hint_range(0.001, 2.0) = 0.03;
uniform float end_radius_m : hint_range(0.001, 4.0) = 0.09;
// Higher = tighter bright core along the axis.
uniform float core_tightness : hint_range(0.5, 16.0) = 4.0;
// How much the core outshines the outer gas.
uniform float core_boost : hint_range(0.0, 4.0) = 0.6;
// Higher = softer, thinner-looking silhouette.
uniform float edge_softness : hint_range(0.5, 8.0) = 1.5;
uniform float tail_falloff : hint_range(0.2, 6.0) = 1.6;
// Fade-in over this share of the length, so the open end of the mesh never shows a hard ring.
uniform float nozzle_fade : hint_range(0.0, 0.5) = 0.06;
// Seen along its axis the cone's walls are edge-on; this keeps it a soft disc that is brightest at the nozzle.
uniform float end_on_glow : hint_range(0.0, 2.0) = 0.8;
uniform float flicker_hz : hint_range(0.0, 60.0) = 24.0;
uniform float flicker_amount : hint_range(0.0, 1.0) = 0.3;
uniform float length_flicker : hint_range(0.0, 0.5) = 0.1;

instance uniform float throttle : hint_range(0.0, 1.0) = 1.0;
instance uniform float seed = 0.0;

varying float axial;
varying float flick;
varying float power;
varying vec3 axis_view;

float hash11(float n) {
    return fract(sin(n * 127.1) * 43758.5453);
}

float flicker(float t, float s) {
    float f = t * flicker_hz + s * 17.0;
    float i = floor(f);
    return mix(hash11(i + s), hash11(i + 1.0 + s), smoothstep(0.0, 1.0, fract(f)));
}

void vertex() {
    // CylinderMesh: y in [-0.5, 0.5]; +0.5 is the nozzle end.
    axial = clamp(0.5 - VERTEX.y, 0.0, 1.0);
    flick = flicker(TIME, seed);
    power = throttle;
    // Short pulses shorten the cone but never squash it into a ball; throttle also scales brightness.
    float plume_length = length_m * mix(0.45, 1.0, throttle) * (1.0 - length_flicker + 2.0 * length_flicker * flick);
    float radius = mix(exit_radius_m, end_radius_m, sqrt(axial));
    vec2 ring = VERTEX.xz * 2.0;
    VERTEX = vec3(axial * plume_length, ring.x * radius, ring.y * radius);
    NORMAL = normalize(vec3(0.0, ring.x, ring.y));
    axis_view = (MODELVIEW_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xyz;
}

void fragment() {
    // Interpolation can push axial a hair past 1 at the open end; pow() of a negative base is NaN, and the
    // view's glow spreads NaN pixels into big white discs. Clamp every pow() base.
    float along = clamp(axial, 0.0, 1.0);
    float facing = clamp(abs(dot(normalize(NORMAL), VIEW)), 0.0, 1.0);
    float end_on = pow(clamp(abs(dot(normalize(axis_view), VIEW)), 0.0, 1.0), 4.0);
    float body = max(pow(facing, edge_softness), end_on * end_on_glow * (1.0 - along));
    float core = clamp(max(pow(facing, core_tightness), end_on) * (1.0 - along), 0.0, 1.0);
    float tail = pow(1.0 - along, tail_falloff) * smoothstep(0.0, max(nozzle_fade, 1e-4), along);
    float intensity = energy * power * (1.0 - flicker_amount + flicker_amount * flick) * body * tail;
    ALBEDO = mix(outer_color.rgb, core_color.rgb * (1.0 + core_boost), core) * intensity;
}
