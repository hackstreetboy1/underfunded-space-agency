// ENV-01 Earth surface (MODELING 1, 10.1; docs/review/gate-3/earth/REVIEW_EARTH.md shader notes).
// Stylized, palette-flat Earth on Godot's SphereMesh: albedo, a separately rotated cloud layer with a
// slight shadow, a restrained ocean-only glint and a soft terminator into a flat dark-blue night side
// (no city lights). Lit only by directional lights through light(); ambient and sky radiance are off so
// the night side is exactly the night colour. The atmosphere shell (earth_atmosphere_rim) draws on top.
//
// Longitude alignment. Godot's SphereMesh puts mesh u = 0 at local +Z and increases u eastward
// (counter-clockwise seen from +Y): u = 0.25 at +X, 0.5 at -Z, 0.75 at -X; v = 0 is the +Y pole.
// The textures are equirectangular with u = 0 at 180 deg W and u = 0.5 at Greenwich. This shader shifts
// u so that, with longitude_offset_deg = 0, Greenwich lies on local +X and longitude increases with a
// positive rotation about local +Y. That is the simulation's convention once mapped by
// SpacecraftAxes.ToGodot (inertial +X -> local +X, spin axis inertial +Z -> local +Y): a point at
// inertial angle phi has longitude phi - theta(t), so set longitude_offset_deg = theta(t) in degrees.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, ambient_light_disabled, shadows_disabled, fog_disabled;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// RGB is the cloud colour, alpha the coverage.
uniform sampler2D cloud_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Data, not colour: 1 = land or ice, 0 = open water.
uniform sampler2D land_mask_texture : hint_default_white, filter_linear_mipmap, repeat_enable;

// Inertial angle of the prime meridian (theta(t)); wrap it on the CPU to keep float precision.
uniform float longitude_offset_deg : hint_range(0.0, 360.0) = 0.0;
// Cloud layer offset over the ground, in turns; positive moves the clouds east.
uniform float cloud_rotation = 0.0;
// Gain on cloud alpha: above 1 thickens thin cloud, below 1 thins everything.
uniform float cloud_coverage : hint_range(0.0, 3.0) = 1.0;
uniform vec4 cloud_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);

uniform float day_energy : hint_range(0.0, 4.0) = 1.0;
// 0 = one flat value over the whole day side, 1 = full Lambert falloff.
uniform float lambert_amount : hint_range(0.0, 1.0) = 0.45;
// Half-width of the day/night blend in N.L (the atmosphere rim uses 0.35 on its larger shell).
uniform float terminator_softness : hint_range(0.01, 1.0) = 0.2;

uniform vec4 night_color : source_color = vec4(0.043, 0.090, 0.188, 1.0);
uniform float night_energy : hint_range(0.0, 8.0) = 1.0;
// How much land and cloud shapes stay readable on the night side.
uniform float night_detail : hint_range(0.0, 1.0) = 0.35;

uniform vec4 glint_color : source_color = vec4(1.0, 0.97, 0.9, 1.0);
uniform float glint_strength : hint_range(0.0, 2.0) = 0.22;
uniform float glint_power : hint_range(4.0, 512.0) = 48.0;

uniform float cloud_shadow_strength : hint_range(0.0, 1.0) = 0.25;
// Ground distance from a cloud to its shadow, radians of arc at full side-light (0.012 ~ 75 km).
uniform float cloud_shadow_offset : hint_range(0.0, 0.05) = 0.012;
// Extra mip levels for the shadow lookup, so shadows are softer than the clouds.
uniform float cloud_shadow_blur : hint_range(0.0, 4.0) = 1.5;

const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

varying vec3 v_east;
varying vec3 v_north;
varying float v_cos_lat;

varying vec2 v_cloud_uv;
varying float v_cloud;
varying float v_ocean;
varying float v_shadow_lod;

void vertex() {
    vec3 p = normalize(VERTEX);
    // cross(+Y, p): the direction of increasing mesh u, degenerate at the poles.
    vec3 east = vec3(p.z, 0.0, -p.x);
    float cos_lat = length(east);
    east = cos_lat > 1e-4 ? east / cos_lat : vec3(1.0, 0.0, 0.0);
    v_cos_lat = cos_lat;
    v_east = normalize((MODELVIEW_MATRIX * vec4(east, 0.0)).xyz);
    v_north = normalize((MODELVIEW_MATRIX * vec4(cross(p, east), 0.0)).xyz);
}

void fragment() {
    // Offsets are added, never wrapped per fragment: UV.x stays continuous inside every triangle and the
    // repeat sampler does the wrap, so no seam appears where the shifted u crosses 1.
    float surface_shift = fract(0.25 - longitude_offset_deg / 360.0);
    vec2 uv = vec2(UV.x + surface_shift, UV.y);
    vec2 cloud_uv = vec2(uv.x - fract(cloud_rotation), uv.y);

    vec3 ground = texture(albedo_texture, uv).rgb;
    float land = texture(land_mask_texture, uv).r;
    vec4 cloud_sample = texture(cloud_texture, cloud_uv);
    float cloud = clamp(cloud_sample.a * cloud_coverage, 0.0, 1.0);

    vec3 albedo = mix(ground, cloud_sample.rgb * cloud_tint.rgb, cloud);
    ALBEDO = albedo;
    ROUGHNESS = 1.0;
    SPECULAR = 0.0;

    float detail = mix(1.0, 0.5 + 2.0 * dot(albedo, LUMA), night_detail);
    EMISSION = night_color.rgb * night_energy * detail;

    vec2 texel = cloud_uv * vec2(textureSize(cloud_texture, 0));
    vec2 dx = dFdx(texel);
    vec2 dy = dFdy(texel);
    v_shadow_lod = max(0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8)), 0.0) + cloud_shadow_blur;
    v_cloud_uv = cloud_uv;
    v_cloud = cloud;
    v_ocean = (1.0 - clamp(land, 0.0, 1.0)) * (1.0 - cloud);
}

void light() {
    if (LIGHT_IS_DIRECTIONAL) {
        vec3 sun = LIGHT_COLOR / PI * ATTENUATION;
        float n_dot_l = dot(NORMAL, LIGHT);
        float day = smoothstep(-terminator_softness, terminator_softness, n_dot_l);
        float shade = mix(1.0, clamp(n_dot_l, 0.0, 1.0), lambert_amount);

        // Sample the cloud a little towards the Sun: that cloud is the one shading this ground point.
        vec2 sun_tangent = vec2(dot(LIGHT, normalize(v_east)), dot(LIGHT, normalize(v_north)));
        vec2 shadow_shift = vec2(sun_tangent.x / (TAU * max(v_cos_lat, 0.15)), -sun_tangent.y / PI);
        vec2 shadow_uv = v_cloud_uv + shadow_shift * cloud_shadow_offset;
        float caster = clamp(textureLod(cloud_texture, shadow_uv, v_shadow_lod).a * cloud_coverage, 0.0, 1.0);
        float cloud_shadow = 1.0 - cloud_shadow_strength * caster * (1.0 - v_cloud);

        DIFFUSE_LIGHT += sun * (day * shade * cloud_shadow * day_energy);

        vec3 half_vector = normalize(LIGHT + VIEW);
        float glint = pow(clamp(dot(NORMAL, half_vector), 0.0, 1.0), glint_power);
        glint *= glint_strength * v_ocean * smoothstep(0.0, 0.25, n_dot_l);
        SPECULAR_LIGHT += sun * glint_color.rgb * glint;
    }
}
