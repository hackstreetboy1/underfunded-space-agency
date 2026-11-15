// ENV-03 ocean (MODELING 10.3): flat #2C6E9B water, lighter crests in hard bands, gentle vertex
// waves and a foam ring around one point (the capsule). Mesh: a 400 m PlaneMesh subdivided 31x31
// (2 k tris), unrotated and unscaled, so model-space normals equal world-space normals.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert, specular_schlick_ggx;

uniform vec4 water_color : source_color = vec4(0.173, 0.431, 0.608, 1.0);
uniform vec4 crest_color : source_color = vec4(0.290, 0.553, 0.722, 1.0);
uniform vec4 foam_color : source_color = vec4(0.918, 0.949, 0.961, 1.0);
uniform float water_roughness : hint_range(0.0, 1.0) = 0.45;
uniform float water_specular : hint_range(0.0, 1.0) = 0.35;

// Three directional swells; the vertex pass moves the mesh, the fragment pass bands the colour.
uniform float wave_height_m : hint_range(0.0, 3.0) = 0.35;
uniform float wave_length_m : hint_range(5.0, 200.0) = 48.0;
uniform vec2 wave_direction = vec2(1.0, 0.35);
uniform float wave_time_scale : hint_range(0.0, 4.0) = 1.0;
// Shorter ripples that only shape the crest bands (never displace geometry).
uniform float ripple_length_m : hint_range(0.5, 40.0) = 9.0;
uniform float ripple_weight : hint_range(0.0, 1.0) = 0.35;
uniform float crest_threshold : hint_range(0.0, 1.0) = 0.58;
uniform int crest_bands : hint_range(1, 4) = 2;

uniform sampler2D normal_noise : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float normal_noise_scale_m : hint_range(1.0, 200.0) = 22.0;
uniform float normal_noise_strength : hint_range(0.0, 2.0) = 0.3;
uniform float normal_noise_speed_mps : hint_range(0.0, 5.0) = 0.6;

// Foam ring around foam_center (world); the presenter grows the radius after splashdown.
uniform vec3 foam_center = vec3(0.0);
uniform float foam_ring_radius_m : hint_range(0.0, 50.0) = 2.2;
uniform float foam_ring_width_m : hint_range(0.0, 5.0) = 0.35;
uniform float foam_inner_fill : hint_range(0.0, 1.0) = 0.25;
uniform float foam_wobble_m : hint_range(0.0, 1.0) = 0.12;
uniform float foam_strength : hint_range(0.0, 1.0) = 1.0;

const float GRAVITY = 9.81;

varying vec3 world_pos;

// x = height, yz = d(height)/dx, d(height)/dz.
vec3 swell(vec2 p, float t, vec2 dir, float length_m, float amp) {
    float k = TAU / length_m;
    float omega = sqrt(GRAVITY * k);
    vec2 d = normalize(dir);
    float phase = dot(d, p) * k - omega * t;
    return vec3(amp * sin(phase), amp * k * cos(phase) * d);
}

vec3 waves(vec2 p, float t) {
    vec2 d = normalize(wave_direction);
    vec2 side = vec2(-d.y, d.x);
    return swell(p, t, d, wave_length_m, wave_height_m * 0.5)
            + swell(p, t * 1.07, normalize(d + side * 0.6), wave_length_m * 0.71, wave_height_m * 0.3)
            + swell(p, t * 0.93, normalize(d - side * 0.8), wave_length_m * 1.37, wave_height_m * 0.2);
}

void vertex() {
    vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vec3 w = waves(wp.xz, TIME * wave_time_scale);
    VERTEX.y += w.x;
    NORMAL = normalize(vec3(-w.y, 1.0, -w.z));
    world_pos = wp + vec3(0.0, w.x, 0.0);
}

void fragment() {
    float t = TIME * wave_time_scale;
    vec2 d = normalize(wave_direction);
    float h = waves(world_pos.xz, t).x / max(wave_height_m, 0.001);
    float ripple = swell(world_pos.xz, t * 1.3, vec2(d.y, -d.x) + d, ripple_length_m, 1.0).x;
    // 0..1 crest height; the bands are hard steps (flat colour fields, no gradients).
    float crest = clamp((h + ripple * ripple_weight) * 0.5 + 0.5, 0.0, 1.0);
    float band = floor(clamp((crest - crest_threshold) / (1.0 - crest_threshold), 0.0, 0.999) * float(crest_bands + 1));
    vec3 albedo = mix(water_color.rgb, crest_color.rgb, band / float(crest_bands));

    vec2 to_center = world_pos.xz - foam_center.xz;
    float dist = length(to_center);
    float angle = atan(to_center.y, to_center.x);
    float wobble = (sin(angle * 5.0 + t * 0.7) + 0.5 * sin(angle * 9.0 - t * 1.1)) * foam_wobble_m;
    float ring_d = abs(dist + wobble - foam_ring_radius_m) - foam_ring_width_m * 0.5;
    float aa = max(fwidth(ring_d), 0.001);
    float ring = 1.0 - smoothstep(-aa, aa, ring_d);
    float inner = foam_inner_fill * (1.0 - smoothstep(foam_ring_radius_m - aa, foam_ring_radius_m + aa, dist + wobble));
    float foam = clamp(max(ring, inner), 0.0, 1.0) * foam_strength;

    ALBEDO = mix(albedo, foam_color.rgb, foam);
    ROUGHNESS = mix(water_roughness, 0.9, foam);
    SPECULAR = mix(water_specular, 0.2, foam);
    METALLIC = 0.0;

    vec2 uv = world_pos.xz / normal_noise_scale_m;
    vec2 drift = d * normal_noise_speed_mps * TIME / normal_noise_scale_m;
    vec3 n1 = texture(normal_noise, uv + drift).rgb;
    vec3 n2 = texture(normal_noise, uv * 1.7 - drift * 0.8).rgb;
    NORMAL_MAP = mix(n1, n2, 0.5);
    NORMAL_MAP_DEPTH = normal_noise_strength * (1.0 - foam);
}
