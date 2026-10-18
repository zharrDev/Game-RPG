// Color grading + vignette — pass terakhir sebelum HUD.
// Palet dibiarkan muted: kontras sedikit naik, saturasi turun, bayangan
// digeser ke coklat hangat, dan tepi layar digelapkan lembut.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform vec2 viewport_size = vec2(480.0, 270.0);
uniform float saturation : hint_range(0.0, 1.5) = 0.92;
uniform float contrast : hint_range(0.5, 1.5) = 1.05;
uniform vec3 lift : source_color = vec3(0.020, 0.014, 0.020);
uniform vec3 gain : source_color = vec3(1.02, 0.99, 0.95);
uniform float warmth : hint_range(0.0, 0.3) = 0.05;
uniform float vignette : hint_range(0.0, 1.5) = 0.55;
// 0 = siang, 1 = malam pekat (diatur Fx saat siklus siang/malam menyala)
uniform float night : hint_range(0.0, 1.0) = 0.0;
uniform vec3 night_tint : source_color = vec3(0.30, 0.36, 0.62);

void fragment() {
    vec3 col = texture(screen_tex, SCREEN_UV).rgb;

    // grading dasar
    col = col * gain + lift;
    col = (col - 0.5) * contrast + 0.5;
    float luma = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(vec3(luma), col, saturation);
    col.r += warmth * (1.0 - luma) * 0.5;
    col.b -= warmth * (1.0 - luma) * 0.15;

    // malam: sedikit ke biru — penggelapan utama dilakukan CanvasModulate (Lighting),
    // jadi faktor di sini kecil saja supaya tidak dobel gelap
    col = mix(col, col * night_tint, night * 0.35);

    // vignette berbentuk lingkaran (koreksi aspek viewport)
    vec2 v = (SCREEN_UV - 0.5) * vec2(viewport_size.x / viewport_size.y, 1.0);
    float d = length(v);
    col *= 1.0 - vignette * smoothstep(0.42, 0.98, d);

    COLOR = vec4(col, 1.0);
}
