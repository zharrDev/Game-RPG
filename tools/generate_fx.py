#!/usr/bin/env python3
"""Generator tekstur pendukung pipeline FX ("Arga: Api & Asap").

Menghasilkan tekstur yang dibaca game/scripts/Fx.cs dan shader di
game/assets/shaders/:

  fx_noise.png       128x128 RGB — value-noise tileable 3 kanal independen
                     (R/G untuk arah kabut, B untuk detail rapat). Dipakai
                     shader asap & heat haze; pinggir kiri/kanan dan
                     atas/bawah menyambung tanpa jahitan.
  fx_light_soft.png  128x128 RGBA — cahaya radial lembut (warm white) untuk
                     PointLight2D obor/lentera/api.
  fx_glow_soft.png   64x64 RGBA — titik glow aditif untuk bloom/bara.
  fx_flame.png       64x16 RGBA — 4 frame nyala 16x16 (pixel-art alpha biner)
                     untuk AnimatedSprite2D di tiap titik api; palet sama
                     dengan FLAME di generate_tileset.py.
  fx_sprout.png      8x8 RGBA — tunas hijau (pixel-art alpha biner) yang
                     tumbuh di zona bekas terbakar.
  fx_vignette.png    480x270 RGBA — vignette HUD: tengah transparan, tepi
                     putih ber-alpha (game me-modulate merah saat terluka /
                     HP rendah). Dipakai Hud.cs (tahap 2g).

Catatan gaya: tekstur di atas bukan pixel-art ber-alpha biner (cahaya & kabut
memang perlu gradasi), jadi tools/verify_assets.py menguji kualitasnya
dengan aturan sendiri: noise harus punya variasi & tileable, cahaya harus
gradasi mulus dari pusat terang ke tepi transparan. fx_flame & fx_sprout
justru pixel-art: alpha biner dan palet terbatas.

Output:
  game/assets/fx/fx_noise.png
  game/assets/fx/fx_light_soft.png
  game/assets/fx/fx_glow_soft.png
  game/assets/fx/fx_flame.png
  game/assets/fx/fx_sprout.png
  game/assets/fx/fx_vignette.png
  docs/preview-fx.png                 pratinjau 4x (manusia)

Jalankan:
    python3 tools/generate_fx.py
"""

from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "game/assets/fx"
DOCS = ROOT / "docs"

NOISE_SIZE = 128
NOISE_CELLS = 8
NOISE_OCTAVES = 3
NOISE_SEEDS = (211, 223, 227)          # R, G, B — independen
LIGHT_SIZE = 128
LIGHT_RGB = (255, 238, 208)            # warm white
GLOW_SIZE = 64

FLAME_SIZE = 16
FLAME_FRAMES = (7, 8, 9, 8)            # tinggi nyala per frame (px)
FLAME_BASE_Y = 14
FLAME_OUTER = (0xE0, 0x8A, 0x3C)       # FLAME[1] di generate_tileset.py
FLAME_CORE = (0xE8, 0xC8, 0x5A)        # FLAME[0]
FLAME_TIP = (0xF5, 0xE7, 0xB0)         # FLAME[2]
SPROUT_SIZE = 8
SPROUT_STEM = (0x4F, 0x7A, 0x33)
SPROUT_LEAF = (0x6F, 0xA3, 0x48)
VIGNETTE_W, VIGNETTE_H = 480, 270     # ukuran viewport game
VIGNETTE_INNER = 0.55                  # radius (ternormalisasi) mulai muncul
VIGNETTE_MAX_ALPHA = 0.62              # alpha maksimum di tepi layar


def smoothstep(t: float) -> float:
    return t * t * (3.0 - 2.0 * t)


def tileable_noise(size: int, cells: int, seed: int, octaves: int = NOISE_OCTAVES) -> list[list[float]]:
    """Value noise periodik: lattice dibungkus (wrap) sehingga tile menyambung."""
    rng = random.Random(seed)
    layers: list[tuple[int, list[list[float]], float]] = []
    amplitude = 1.0
    total = 0.0
    c = cells
    for _ in range(octaves):
        values = [[rng.random() for _ in range(c)] for _ in range(c)]
        layers.append((c, values, amplitude))
        total += amplitude
        amplitude *= 0.5
        c *= 2
    out: list[list[float]] = [[0.0] * size for _ in range(size)]
    for y in range(size):
        for x in range(size):
            acc = 0.0
            for (lattice, values, amp) in layers:
                gx = x / size * lattice
                gy = y / size * lattice
                x0 = int(gx) % lattice
                y0 = int(gy) % lattice
                x1 = (x0 + 1) % lattice
                y1 = (y0 + 1) % lattice
                tx = smoothstep(gx - math.floor(gx))
                ty = smoothstep(gy - math.floor(gy))
                top = values[y0][x0] * (1.0 - tx) + values[y0][x1] * tx
                bottom = values[y1][x0] * (1.0 - tx) + values[y1][x1] * tx
                acc += (top * (1.0 - ty) + bottom * ty) * amp
            out[y][x] = acc / total
    return out


def make_noise() -> Image.Image:
    channels = [tileable_noise(NOISE_SIZE, NOISE_CELLS, seed) for seed in NOISE_SEEDS]
    image = Image.new("RGBA", (NOISE_SIZE, NOISE_SIZE))
    px = []
    for y in range(NOISE_SIZE):
        for x in range(NOISE_SIZE):
            px.append((
                int(channels[0][y][x] * 255),
                int(channels[1][y][x] * 255),
                int(channels[2][y][x] * 255),
                255,
            ))
    image.putdata(px)
    return image


def make_light(size: int, rgb: tuple[int, int, int], falloff: float) -> Image.Image:
    """Radial gradient: alpha = (1 - d)^falloff, tepi benar-benar transparan."""
    image = Image.new("RGBA", (size, size))
    cx = (size - 1) / 2.0
    px = []
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - cx, y - cx) / cx
            a = 0.0 if d >= 1.0 else (1.0 - d) ** falloff
            px.append((rgb[0], rgb[1], rgb[2], int(round(a * 255))))
    image.putdata(px)
    return image


def make_flame() -> Image.Image:
    """4 frame nyala 16x16 (satu baris). Alpha biner, 3 warna FLAME + dasar bara."""
    sheet = Image.new("RGBA", (FLAME_SIZE * len(FLAME_FRAMES), FLAME_SIZE))
    for f, height in enumerate(FLAME_FRAMES):
        frame = Image.new("RGBA", (FLAME_SIZE, FLAME_SIZE))
        px = frame.load()
        top = FLAME_BASE_Y - height
        for y in range(top, FLAME_BASE_Y + 1):
            t = (y - top) / max(1, height)             # 0 = ujung atas
            half = 1 + int(round(2.1 * t))             # melebar ke bawah
            wobble = 1 if (y + f) % 4 == 0 else (-1 if (y + f) % 4 == 2 else 0)
            cx = FLAME_SIZE // 2 + wobble
            for x in range(cx - half, cx + half + 1):
                px[x, y] = (*FLAME_OUTER, 255)
        # inti kuning di tengah-bawah (makin ke bawah makin besar)
        core_top = top + max(2, height // 2)
        for y in range(core_top, FLAME_BASE_Y + 1):
            t = (y - core_top) / max(1, FLAME_BASE_Y - core_top)
            half = int(round(0.6 + 1.4 * t))
            for x in range(FLAME_SIZE // 2 - half - 1, FLAME_SIZE // 2 + half):
                if 0 <= x < FLAME_SIZE and px[x, y][3]:
                    px[x, y] = (*FLAME_CORE, 255)
        # ujung pucat satu baris di puncak
        tip_x = FLAME_SIZE // 2 + (1 if f % 2 else -1)
        if px[tip_x, top][3]:
            px[tip_x, top] = (*FLAME_TIP, 255)
        # dasar bara (kayu membara) di baris terbawah
        for x in range(FLAME_SIZE // 2 - 3, FLAME_SIZE // 2 + 3):
            px[x, FLAME_BASE_Y] = (*FLAME_CORE, 255)
        sheet.alpha_composite(frame, (f * FLAME_SIZE, 0))
    return sheet


def make_sprout() -> Image.Image:
    """Tunas 8x8: batang + dua daun (alpha biner, 2 hijau)."""
    image = Image.new("RGBA", (SPROUT_SIZE, SPROUT_SIZE))
    px = image.load()
    for y in range(4, 8):
        px[4, y] = (*SPROUT_STEM, 255)
    px[4, 4] = (*SPROUT_LEAF, 255)
    for x, y in ((2, 4), (3, 3), (3, 4), (2, 5)):
        px[x, y] = (*SPROUT_LEAF, 255)
    for x, y in ((5, 3), (6, 4), (5, 4), (6, 5)):
        px[x, y] = (*SPROUT_LEAF, 255)
    px[3, 6] = (*SPROUT_STEM, 255)
    px[5, 6] = (*SPROUT_STEM, 255)
    return image


def make_vignette() -> Image.Image:
    """Vignette 480x270 untuk HUD (tahap 2g): tengah transparan, tepi putih+alpha.

    Warnanya putih supaya game bisa me-modulate (merah saat terluka / HP rendah);
    alpha naik halus dari 0 di tengah ke VIGNETTE_MAX_ALPHA di tepi layar.
    """
    image = Image.new("RGBA", (VIGNETTE_W, VIGNETTE_H))
    cx, cy = (VIGNETTE_W - 1) / 2.0, (VIGNETTE_H - 1) / 2.0
    px = []
    for y in range(VIGNETTE_H):
        for x in range(VIGNETTE_W):
            dx = (x - cx) / cx
            dy = (y - cy) / cy
            d = min(1.0, math.hypot(dx, dy))
            t = 0.0 if d <= VIGNETTE_INNER else (d - VIGNETTE_INNER) / (1.0 - VIGNETTE_INNER)
            a = int(round((t ** 1.6) * VIGNETTE_MAX_ALPHA * 255))
            px.append((255, 255, 255, a))
    image.putdata(px)
    return image


def build_preview(noise: Image.Image, light: Image.Image, glow: Image.Image,
                  flame: Image.Image, sprout: Image.Image, vignette: Image.Image) -> None:
    scale = 4
    pad = 8
    label_h = 12
    cell_w = NOISE_SIZE * scale * 2 + pad * 3
    cell_h = NOISE_SIZE * scale * 2 + label_h + pad * 2 + VIGNETTE_H + label_h + pad
    preview = Image.new("RGBA", (cell_w, cell_h), (0x1E, 0x1E, 0x26, 255))
    noise_tiled = Image.new("RGBA", (NOISE_SIZE * 2, NOISE_SIZE * 2))
    for ty in range(2):
        for tx in range(2):
            noise_tiled.alpha_composite(noise, (tx * NOISE_SIZE, ty * NOISE_SIZE))
    preview.alpha_composite(noise_tiled.resize((NOISE_SIZE * scale * 2, NOISE_SIZE * scale * 2),
                                               Image.NEAREST), (pad, pad + label_h))
    light_big = light.resize((LIGHT_SIZE * scale, LIGHT_SIZE * scale), Image.NEAREST)
    preview.alpha_composite(light_big, (pad * 2 + NOISE_SIZE * scale * 2, pad + label_h))
    glow_big = glow.resize((GLOW_SIZE * scale, GLOW_SIZE * scale), Image.NEAREST)
    preview.alpha_composite(glow_big, (pad * 2 + NOISE_SIZE * scale * 2, pad * 2 + label_h + LIGHT_SIZE * scale))
    draw = ImageDraw.Draw(preview)
    draw.text((pad, pad), "fx_noise 128x128 tileable 2x2 (4x)", fill=(0xE8, 0xC8, 0x5A, 255))
    draw.text((pad * 2 + NOISE_SIZE * scale * 2, pad), "fx_light_soft 128 (4x)", fill=(0xE8, 0xC8, 0x5A, 255))
    draw.text((pad * 2 + NOISE_SIZE * scale * 2, pad * 2 + LIGHT_SIZE * scale),
              "fx_glow_soft 64 (4x)", fill=(0xE8, 0xC8, 0x5A, 255))

    # baris bawah kolom 2: lembar nyala + tunas
    fx_y = pad * 2 + LIGHT_SIZE * scale + GLOW_SIZE * scale + pad
    flame_big = flame.resize((flame.width * scale, flame.height * scale), Image.NEAREST)
    sprout_big = sprout.resize((sprout.width * scale * 3, sprout.height * scale * 3), Image.NEAREST)
    preview.alpha_composite(flame_big, (pad * 2 + NOISE_SIZE * scale * 2, fx_y + label_h))
    preview.alpha_composite(sprout_big, (pad * 2 + NOISE_SIZE * scale * 2, fx_y + label_h + flame_big.height + pad))
    draw.text((pad * 2 + NOISE_SIZE * scale * 2, fx_y),
              "fx_flame 4 frame 16x16 (4x) + fx_sprout 8x8 (12x)", fill=(0xE8, 0xC8, 0x5A, 255))

    # vignette HUD: tampilkan di atas kotak gelap supaya alpha tepi terlihat
    vig_y = fx_y + label_h + flame_big.height + pad + sprout_big.height + pad
    vig_bg = Image.new("RGBA", (vignette.width, vignette.height), (0x3A, 0x4A, 0x38, 255))
    vig_bg.alpha_composite(ImageChops.multiply(vignette, Image.new("RGBA", vignette.size, (0xFF, 0x66, 0x66, 255))))
    preview.alpha_composite(vig_bg, (pad, vig_y + label_h))
    draw.text((pad, vig_y), "fx_vignette 480x270 (modulate merah)", fill=(0xE8, 0xC8, 0x5A, 255))
    preview.convert("RGB").save(DOCS / "preview-fx.png")


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    DOCS.mkdir(parents=True, exist_ok=True)
    noise = make_noise()
    light = make_light(LIGHT_SIZE, LIGHT_RGB, falloff=2.2)
    glow = make_light(GLOW_SIZE, (255, 245, 220), falloff=2.8)
    flame = make_flame()
    sprout = make_sprout()
    vignette = make_vignette()
    noise.save(OUT_DIR / "fx_noise.png")
    light.save(OUT_DIR / "fx_light_soft.png")
    glow.save(OUT_DIR / "fx_glow_soft.png")
    flame.save(OUT_DIR / "fx_flame.png")
    sprout.save(OUT_DIR / "fx_sprout.png")
    vignette.save(OUT_DIR / "fx_vignette.png")
    build_preview(noise, light, glow, flame, sprout, vignette)
    print(f"fx_noise.png      : {noise.width}x{noise.height} (3 kanal noise tileable)")
    print(f"fx_light_soft.png : {light.width}x{light.height} (cahaya radial warm)")
    print(f"fx_glow_soft.png  : {glow.width}x{glow.height} (titik glow)")
    print(f"fx_flame.png      : {flame.width}x{flame.height} ({len(FLAME_FRAMES)} frame nyala)")
    print(f"fx_sprout.png     : {sprout.width}x{sprout.height} (tunas zona terbakar)")
    print(f"fx_vignette.png   : {vignette.width}x{vignette.height} (vignette HUD, alpha tepi {VIGNETTE_MAX_ALPHA})")
    print(f"preview: {DOCS / 'preview-fx.png'}")


if __name__ == "__main__":
    main()
