// Heat haze lokal: dipasang sebagai Sprite2D (tekstur 1x1 putih, di-scale)
// di atas titik api. UV = koordinat lokal quad, SCREEN_UV = layar, jadi
// distorsi hanya terasa di sekitar api dan meredup ke tepi quad.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform sampler2D noise_tex : repeat_enable, filter_nearest;
uniform vec2 viewport_size = vec2(480.0, 270.0);
uniform float strength : hint_range(0.0, 4.0) = 1.6;   // amplitudo dalam piksel
uniform float speed = 0.8;
uniform float seed = 0.0;

void fragment() {
    vec2 px = 1.0 / viewport_size;
    // mask radial: penuh di pusat quad, nol di tepi
    float mask = smoothstep(0.5, 0.10, distance(UV, vec2(0.5)));
    // dua arah dari kanal R/G noise yang independen
    vec2 n = texture(noise_tex, UV * 1.4 + vec2(seed, TIME * speed)).rg * 2.0 - 1.0;
    vec2 n2 = texture(noise_tex, UV * 2.6 - vec2(TIME * speed * 0.6, seed)).rg * 2.0 - 1.0;
    vec2 off = (n * 0.7 + n2 * 0.3) * strength * px * mask;
    // udara panas naik: sampel sedikit dari bawah supaya gelembung ke atas
    vec2 rise = vec2(0.0, -0.35 * px.y * mask);
    COLOR = texture(screen_tex, SCREEN_UV + off + rise);
}
