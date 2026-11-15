// VFX-02c splash foam ring: an expanding, fading ring on a flat plane (unit PlaneMesh scaled to the full ring size).
// progress is driven by SplashVfx (0 = just splashed, 1 = gone); reduced motion stretches it, never flickers.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform vec4 foam_color : source_color = vec4(0.95, 0.98, 1.0, 1.0);
uniform float progress : hint_range(0.0, 1.0) = 1.0;
uniform float start_radius : hint_range(0.0, 1.0) = 0.12;
uniform float ring_width : hint_range(0.02, 0.5) = 0.14;

void fragment() {
    float d = length(UV - vec2(0.5)) * 2.0;
    float radius = mix(start_radius, 0.95, 1.0 - pow(1.0 - progress, 2.0));
    float band = 1.0 - smoothstep(0.0, ring_width, abs(d - radius));
    float fade = (1.0 - progress) * smoothstep(1.0, 0.85, d);
    ALBEDO = foam_color.rgb;
    ALPHA = band * fade * foam_color.a * 0.85;
}
