// Bloom ringan untuk 2D: bright-pass 9 tap (2 radius) lalu ditambahkan
// kembali secara aditif. Murah untuk viewport 480x270 dan tetap memakai
// sampling nearest supaya tidak mengaburkan pixel art.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform vec2 viewport_size = vec2(480.0, 270.0);
uniform float threshold : hint_range(0.0, 1.0) = 0.68;
uniform float intensity : hint_range(0.0, 2.0) = 0.55;
uniform vec3 glow_color : source_color = vec3(1.0, 0.88, 0.66);

float bright(vec2 uv) {
    vec3 c = texture(screen_tex, uv).rgb;
    float luma = dot(c, vec3(0.299, 0.587, 0.114));
    return max(0.0, luma - threshold) / (1.0 - threshold);
}

void fragment() {
    vec2 px = 1.0 / viewport_size;
    vec3 base = texture(screen_tex, SCREEN_UV).rgb;
    float sum = 0.0;
    // inti terang
    sum += bright(SCREEN_UV) * 2.0;
    // radius dekat (2 px) — 4 tap
    sum += bright(SCREEN_UV + vec2(px.x * 2.0, 0.0));
    sum += bright(SCREEN_UV - vec2(px.x * 2.0, 0.0));
    sum += bright(SCREEN_UV + vec2(0.0, px.y * 2.0));
    sum += bright(SCREEN_UV - vec2(0.0, px.y * 2.0));
    // radius jauh (5 px) — 4 tap diagonal
    sum += bright(SCREEN_UV + vec2(px.x * 5.0, px.y * 5.0)) * 0.6;
    sum += bright(SCREEN_UV + vec2(-px.x * 5.0, px.y * 5.0)) * 0.6;
    sum += bright(SCREEN_UV + vec2(px.x * 5.0, -px.y * 5.0)) * 0.6;
    sum += bright(SCREEN_UV + vec2(-px.x * 5.0, -px.y * 5.0)) * 0.6;
    float glow = sum / 7.4;
    vec3 col = base + glow_color * glow * intensity;
    COLOR = vec4(col, 1.0);
}
