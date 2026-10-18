// Asap/kabut berlapis — dua oktaf noise yang bergerak pelan dan
// digeser oleh posisi kamera supaya kabut terasa menempel di dunia,
// bukan di layar. Warna abu-arang, kuatnya diatur uniform `ash`.
// Dipakai Fx.cs pada ColorRect full-screen (layer di bawah HUD).
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform sampler2D noise_tex : repeat_enable, filter_nearest;
uniform vec2 viewport_size = vec2(480.0, 270.0);
uniform vec2 cam_px = vec2(0.0);
uniform float ash : hint_range(0.0, 0.75) = 0.20;
uniform float wind = 0.035;
uniform vec3 smoke_color : source_color = vec3(0.68, 0.67, 0.63);

float smoke_density(vec2 world, float t) {
    // lapis jauh: bergerak lambat, skala besar
    vec2 uv1 = world / 256.0 + vec2(t * wind, t * wind * 0.30);
    // lapis dekat: lebih rapat, arah sedikit berbeda
    vec2 uv2 = world / 97.0 - vec2(t * wind * 0.65, t * wind * 0.45);
    float far = texture(noise_tex, uv1).r;
    float near = texture(noise_tex, uv2).g;
    float density = far * 0.62 + near * 0.38;
    // ambil hanya bagian tebal, lalu kuantisasi 6 langkah (gaya pixel art)
    density = smoothstep(0.38, 0.88, density);
    return floor(density * 6.0) / 6.0;
}

void fragment() {
    vec2 world = cam_px + (SCREEN_UV - 0.5) * viewport_size;
    float density = smoke_density(world, TIME);
    vec3 col = texture(screen_tex, SCREEN_UV).rgb;
    // kabut menipis di bagian sangat terang (api) supaya lidah api tetap terbaca
    float luma = dot(col, vec3(0.299, 0.587, 0.114));
    float keep = 1.0 - smoothstep(0.72, 1.05, luma) * 0.55;
    col = mix(col, smoke_color, density * ash * keep);
    COLOR = vec4(col, 1.0);
}
