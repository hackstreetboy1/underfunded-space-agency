// ENV-02 starfield (MODELING 10.2): sparse procedural stars, mostly dim, a few bright; no Milky
// Way. Static on purpose: TIME in a sky shader re-renders the radiance map every frame. The
// radiance/cubemap passes get the plain background so stars never show up in reflections.
shader_type sky;

uniform vec4 space_color : source_color = vec4(0.012, 0.016, 0.031, 1.0);
uniform vec4 warm_tint : source_color = vec4(1.0, 0.93, 0.82, 1.0);
uniform vec4 cool_tint : source_color = vec4(0.82, 0.89, 1.0, 1.0);

// Cells across the unit sphere; probability is the share of cells holding a star.
uniform float dim_density : hint_range(10.0, 400.0) = 100.0;
uniform float dim_probability : hint_range(0.0, 1.0) = 0.012;
uniform float dim_size_deg : hint_range(0.005, 0.5) = 0.045;
uniform float dim_energy : hint_range(0.0, 8.0) = 0.7;

uniform float bright_density : hint_range(5.0, 200.0) = 40.0;
uniform float bright_probability : hint_range(0.0, 1.0) = 0.005;
uniform float bright_size_deg : hint_range(0.005, 1.0) = 0.09;
uniform float bright_energy : hint_range(0.0, 16.0) = 4.0;

const float DEG_PER_RAD = 57.29578;

float hash13(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

vec3 hash33(vec3 p) {
    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.xxy + p.yxx) * p.zyx);
}

// At most one star per cell of a 3D grid scaled to the sphere (about 12.6 * density^2 * probability
// stars per layer); the 27 neighbouring cells are checked.
vec3 star_layer(vec3 dir, float density, float probability, float size_deg, float energy, float seed) {
    vec3 p = dir * density;
    vec3 base = floor(p);
    vec3 result = vec3(0.0);
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            for (int z = -1; z <= 1; z++) {
                vec3 cell = base + vec3(float(x), float(y), float(z));
                if (hash13(cell + seed) > probability) {
                    continue;
                }
                vec3 rnd = hash33(cell + seed * 1.7);
                vec3 star_pos = cell + 0.2 + 0.6 * rnd;
                // Only stars in a one-cell shell around the sphere: every pixel near such a star
                // sees its cell among the neighbours, so no star is cut in half.
                if (abs(length(star_pos) - density) > 0.5) {
                    continue;
                }
                vec3 star_dir = normalize(star_pos);
                float angle_deg = length(dir - star_dir) * DEG_PER_RAD;
                // Most stars dim, a few brighter: brightness skewed towards 0.
                float brightness = 0.25 + 0.75 * rnd.x * rnd.x * rnd.x;
                float size = size_deg * (0.7 + 0.6 * rnd.y);
                float glow = exp(-(angle_deg * angle_deg) / (size * size));
                vec3 tint = mix(cool_tint.rgb, warm_tint.rgb, rnd.z);
                result += tint * glow * brightness * energy;
            }
        }
    }
    return result;
}

void sky() {
    if (AT_CUBEMAP_PASS) {
        COLOR = space_color.rgb;
    } else {
        vec3 dir = normalize(EYEDIR);
        COLOR = space_color.rgb
                + star_layer(dir, dim_density, dim_probability, dim_size_deg, dim_energy, 0.0)
                + star_layer(dir, bright_density, bright_probability, bright_size_deg, bright_energy, 71.0);
    }
}
