#!/usr/bin/env python3
"""Pratinjau before/after tiap tahap peningkatan visual -> docs/.

Karena Godot di lingkungan tes bisa headless (tanpa layar), pratinjau di sini
disimulasikan deterministik dari aset yang sama dengan game:
  · before = render dunia apa adanya (ground + prop, urutan y-sort sama)
  · after  = render + efek tahap (asap berlapis, heat haze, bloom, grading)

Parameter efek mengikuti shader di game/assets/shaders/ supaya yang terlihat di
docs mendekati layar sungguhan. Untuk screenshot asli Godot, jalankan game
dengan `--shot=docs/namafile.png` (butuh layar).

Output:
  tahap 2a — pipeline FX:
    docs/preview-stage-2a-before.png   480x270 (1x viewport) x2 = 960x540
    docs/preview-stage-2a-after.png
    docs/preview-stage-2a-compare.png
  tahap 2b — siklus siang/malam + cahaya api:
    docs/preview-stage-2b-before.png   (siang, hasil akhir 2a)
    docs/preview-stage-2b-after.png    (malam + PointLight2D di 9 titik api)
    docs/preview-stage-2b-compare.png
  tahap 2c — api/bara/abu/tunas (senja):
    docs/preview-stage-2c-before.png   (akhir 2b: malam + cahaya api)
    docs/preview-stage-2c-after.png    (nyala 4-frame, bara, abu & tunas)
    docs/preview-stage-2c-compare.png
  tahap 2d — perbaikan tile (senja, efek 2c):
    docs/preview-stage-2d-before.png   (akhir 2c: zona hangus polos, riak seragam)
    docs/preview-stage-2d-after.png    (tanah hangus di zona terbakar + riak menjalar)
    docs/preview-stage-2d-compare.png
    docs/preview-stage-2d-water-*.png  (crop tepi sungai: bukti riak tidak seragam)
  tahap 2f — game feel (mock dari aset asli):
    docs/preview-stage-2f-before.png   (akhir 2e: hero lari tanpa efek)
    docs/preview-stage-2f-after.png    (+ debu langkah, percikan peti, trail)
    docs/preview-stage-2f-compare.png

Jalankan:
    python3 tools/preview_stages.py
"""

from __future__ import annotations

import json
import math
import random
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
TILES_DIR = ROOT / "game/assets/tiles"
WORLD_DIR = ROOT / "game/assets/world"
FX_DIR = ROOT / "game/assets/fx"
CHAR_DIR = ROOT / "game/assets/character"
DOCS = ROOT / "docs"

TILE = 16
VIEW_W, VIEW_H = 480, 270
SCALE = 2
FIRE_GROUPS = ("torch", "lantern", "campfire_1")

ASH = 0.20
SMOKE_COLOR = (0.68, 0.67, 0.63)
GLOW_THRESHOLD = 0.68
GLOW_INTENSITY = 0.55
GLOW_COLOR = (1.0, 0.88, 0.66)
VIGNETTE = 0.55

# --- tahap 2b: lighting (samakan dengan game/scripts/Lighting.cs) ---
TIME_OF_DAY = 0.06                 # 0 = tengah malam, 0.5 = tengah hari
NIGHT_COLOR = (0.42, 0.47, 0.68)   # Lighting.NightColor (CanvasModulate)
NIGHT_TINT = (0.30, 0.36, 0.62)    # fx_grade: night_tint (faktor 0.35)
LIGHT_COLOR = (1.0, 0.78, 0.5)     # Lighting.FlameColor
LIGHT_ENERGY = 0.85                # energi puncak = LIGHT_ENERGY * (0.3 + 0.7 * night)


def load_world() -> tuple[dict, dict, Image.Image]:
    data = json.loads((WORLD_DIR / "overworld.json").read_text())
    tileset = json.loads((TILES_DIR / "tileset.json").read_text())
    atlas = Image.open(TILES_DIR / "tileset.png").convert("RGBA")
    return data, tileset, atlas


def hash01(x: int, y: int, salt: int) -> float:
    """Sama dengan World.Hash01 (C#): hash deterministik 0..1 dari koordinat tile."""
    h = ((x * 73856093) & 0xFFFFFFFF) ^ ((y * 19349663) & 0xFFFFFFFF) ^ ((salt * 83492791) & 0xFFFFFFFF)
    h ^= h >> 13
    h = (h * 0x5BD1E995) & 0xFFFFFFFF
    h ^= h >> 15
    return (h & 0xFFFFFF) / 0xFFFFFF


def ripple_variant(name: str, x: int, y: int, tileset: dict) -> str:
    """Varian riak per sel (tahap 2d): offset hash 0..7, sama dengan World._waterOffset."""
    offset = int(hash01(x, y, 91) * 8)
    if name.startswith("water_deep"):
        return f"water_deep_{offset % 3}"
    if name.startswith("water_") and offset % 2 == 1 and name + "_b" in tileset["tiles"]:
        return name + "_b"
    return name


def scorched_variant(data: dict, base_name: str, x: int, y: int) -> str | None:
    """Tanah hangus (tahap 2d): hanya grass/meadow di zona terbakar, tepi ragged.

    Logika sama dengan World.Build (salt 77 = tepi, salt 78 = pilih varian).
    """
    if not (base_name.startswith("grass") or base_name.startswith("meadow")):
        return None
    for bx, by, bw, bh in burnt_zones(data):
        if bx <= x < bx + bw and by <= y < by + bh:
            edge = x == bx or x == bx + bw - 1 or y == by or y == by + bh - 1
            if edge and hash01(x, y, 77) < 0.45:
                return None
            return f"scorched_{1 + int(hash01(x, y, 78) * 3) % 3}"
    return None


def render_world(data: dict, tileset: dict, atlas: Image.Image,
                 scorch: bool = False, ripple: bool = False) -> Image.Image:
    """Sama seperti render generate_world_map.py: ground dulu, prop diurut y-sort.

    scorch/ripple = kondisi akhir tahap 2d (tanah hangus + riak air per-sel);
    untuk preview tahap lama biarkan False supaya hasilnya tidak berubah.
    """
    width, height = data["width"], data["height"]
    out = Image.new("RGBA", (width * TILE, height * TILE), (0, 0, 0, 255))

    def blit(name: str, col: int, row: int) -> None:
        t = tileset["tiles"][name]
        crop = atlas.crop((t["x"], t["y"], t["x"] + t["w"] * TILE, t["y"] + t["h"] * TILE))
        out.alpha_composite(crop, (col * TILE, row * TILE))

    for y in range(height):
        for x in range(width):
            base_name = data["tile_names"][data["ground_tiles"][y][x]]
            name = ripple_variant(base_name, x, y, tileset) if ripple else base_name
            if scorch:
                burned = scorched_variant(data, base_name, x, y)
                if burned is not None:
                    name = burned
            blit(name, x, y)

    cells_by_group: dict[str, list[dict]] = {}
    for cell in tileset["cells"]:
        cells_by_group.setdefault(cell["group"], []).append(cell)
    ordered = sorted(data["props"],
                     key=lambda p: (p["row"] + tileset["tiles"][p["group"]]["base_row"], p["col"]))
    for prop in ordered:
        for cell in cells_by_group[prop["group"]]:
            rx, ry, rw, rh = cell["rect"]
            blit_crop = atlas.crop((rx, ry, rx + rw, ry + rh))
            out.alpha_composite(blit_crop, ((prop["col"] + cell["dx"]) * TILE,
                                            (prop["row"] + cell["dy"]) * TILE))
    return out


def heat_sources(data: dict, tileset: dict) -> list[tuple[float, float, float]]:
    out = []
    for prop in data["props"]:
        group = prop["group"]
        if group not in FIRE_GROUPS:
            continue
        t = tileset["tiles"][group]
        x = (prop["col"] + t["w"] / 2.0) * TILE
        y = (prop["row"] + t["h"]) * TILE - 9.0
        size = 52.0 if group == "campfire_1" else 44.0
        out.append((x, y, size))
    return out


def zone_rects(data: dict) -> dict[str, tuple[int, int, int, int]]:
    return {z["id"]: tuple(z["rect"]) for z in data["markers"]["zones"]}


def pick_water_shores(data: dict) -> tuple[int, int]:
    """Sel pantai (air dangkal) dengan tetangga darat terbanyak — spot busa paling ramai."""
    best = (0, data["width"] // 2, data["height"] // 2)
    for y in range(1, data["height"] - 1):
        for x in range(1, data["width"] - 1):
            name = data["tile_names"][data["ground_tiles"][y][x]]
            if not name.startswith("water_") or name.startswith("water_deep"):
                continue
            shore = 0
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nb = data["tile_names"][data["ground_tiles"][y + dy][x + dx]]
                if not nb.startswith("water"):
                    shore += 1
            if shore > best[0]:
                best = (shore, x, y)
    _, x, y = best
    return x, y


def pick_crop(data: dict, fires: list[tuple[float, float, float]]) -> tuple[int, int]:
    """Pusatkan crop pada api pertama di desa (biar heat haze & asap terlihat)."""
    rect = zone_rects(data).get("greenwood_vale")
    if rect:
        x0, y0, w, h = rect
        for fx, fy, _size in fires:
            if x0 * TILE <= fx <= (x0 + w) * TILE and y0 * TILE <= fy <= (y0 + h) * TILE:
                cx, cy = int(fx), int(fy)
                break
        else:
            spawn = data["markers"]["spawn"]
            cx, cy = (spawn["col"] + 0.5) * TILE, (spawn["row"] + 0.5) * TILE
    else:
        cx = cy = 0
    map_w = data["width"] * TILE
    map_h = data["height"] * TILE
    cx = max(VIEW_W // 2, min(map_w - VIEW_W // 2, cx))
    cy = max(VIEW_H // 2, min(map_h - VIEW_H // 2, cy))
    return int(cx - VIEW_W // 2), int(cy - VIEW_H // 2)


# --------------------------------------------------------------------------- #
# simulasi efek (parameter disamakan dengan shader di game/assets/shaders/)
# --------------------------------------------------------------------------- #
def sample_noise(noise: list[list[tuple[int, int, int]]], wx: float, wy: float, ox: float, oy: float) -> tuple[float, float]:
    size = len(noise)
    x = int((wx + ox) * 0.5) % size
    y = int((wy + oy) * 0.5) % size
    r, g, _b = noise[y][x]
    return r / 255.0, g / 255.0


def apply_smoke(img: Image.Image, noise: list[list[tuple[int, int, int]]], origin: tuple[int, int]) -> Image.Image:
    """Dua lapis noise -> smoothstep -> kuantisasi 6 langkah (gaya fx_smoke)."""
    px = img.load()
    ox, oy = origin
    for y in range(img.height):
        wy = oy + y
        for x in range(img.width):
            wx = ox + x
            far_r, _ = sample_noise(noise, wx, wy, 0.0, 0.0)
            _, near_g = sample_noise(noise, wx, wy, 37.0, 91.0)
            density = far_r * 0.62 + near_g * 0.38
            density = smoothstep(0.38, 0.88, density)
            density = math.floor(density * 6.0) / 6.0
            if density <= 0.0:
                continue
            r, g, b, a = px[x, y]
            luma = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
            keep = 1.0 - smoothstep(0.72, 1.05, luma) * 0.55
            t = density * ASH * keep
            px[x, y] = (
                int(r + (SMOKE_COLOR[0] * 255 - r) * t),
                int(g + (SMOKE_COLOR[1] * 255 - g) * t),
                int(b + (SMOKE_COLOR[2] * 255 - b) * t),
                a,
            )
    return img


def smoothstep(edge0: float, edge1: float, x: float) -> float:
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def apply_heat(img: Image.Image, fires: list[tuple[float, float, float]], origin: tuple[int, int],
               noise: list[list[tuple[int, int, int]]]) -> Image.Image:
    """Distorsi lokal ±1 px di sekitar tiap api (fx_heat)."""
    ox, oy = origin
    for fx, fy, size in fires:
        cx, cy = int(fx - ox), int(fy - oy)
        half = int(size / 2)
        if cx + half < 0 or cy + half < 0 or cx - half > img.width or cy - half > img.height:
            continue
        box = (max(0, cx - half), max(0, cy - half),
               min(img.width, cx + half), min(img.height, cy + half))
        if box[2] - box[0] < 4 or box[3] - box[1] < 4:
            continue
        patch = img.crop(box)
        warped = Image.new("RGBA", patch.size)
        w, h = patch.size
        for py in range(h):
            fy_norm = (py - h / 2.0) / (h / 2.0)
            mask = max(0.0, 1.0 - math.hypot(0.0, fy_norm))
            row = patch.crop((0, py, w, py + 1))
            shift_x = int(round(math.sin(py * 0.35 + fx * 0.01) * 1.0 * mask))
            warped.alpha_composite(row, (shift_x, py))
        img.alpha_composite(warped, (box[0], box[1]))
    return img


def apply_glow(img: Image.Image) -> Image.Image:
    """Bright-pass + dua radius blur, ditambah aditif (fx_glow)."""
    rgb = img.convert("RGB")
    threshold = int(GLOW_THRESHOLD * 255)
    bright = rgb.point(lambda v: max(0, v - threshold))
    near = bright.filter(ImageFilter.GaussianBlur(2.0))
    far = bright.filter(ImageFilter.GaussianBlur(5.0))
    glow = Image.blend(near, far, 0.4)
    glow = glow.point(lambda v: int(v * GLOW_INTENSITY))
    tint = ImageChops.multiply(glow, Image.new("RGB", glow.size,
                                               tuple(int(c * 255) for c in GLOW_COLOR)))
    return ImageChops.add(rgb, tint).convert("RGBA")


def apply_grade(img: Image.Image, night: float = 0.0) -> Image.Image:
    """Kontras + saturasi + lift/gain hangat + tint malam + vignette (fx_grade)."""
    rgb = img.convert("RGB")
    # contrast 1.05 di sekitar 0.5, lalu lift/gain
    def curve(v: int) -> int:
        x = ((v / 255.0 - 0.5) * 1.05 + 0.5) * 1.02 + 0.018
        return max(0, min(255, int(round(x * 255))))
    rgb = rgb.point(curve)
    # saturasi 0.92
    gray = rgb.convert("L")
    rgb = Image.blend(gray.convert("RGB"), rgb, 0.92)
    # malam: sedikit ke biru (pelengkap CanvasModulate)
    if night > 0.001:
        tint = Image.new("RGB", rgb.size, tuple(int(c * 255) for c in NIGHT_TINT))
        rgb = Image.blend(rgb, ImageChops.multiply(rgb, tint), 0.35 * night)
    # vignette radial (koreksi aspek)
    mask = Image.new("L", (img.width, img.height))
    mpx = mask.load()
    aspect = img.width / img.height
    for y in range(img.height):
        for x in range(img.width):
            vx = ((x + 0.5) / img.width - 0.5) * aspect
            vy = (y + 0.5) / img.height - 0.5
            d = math.hypot(vx, vy)
            mpx[x, y] = max(0, min(255, int(round((1.0 - VIGNETTE * smoothstep(0.42, 0.98, d)) * 255))))
    rgb = ImageChops.multiply(rgb, Image.merge("RGB", (mask, mask, mask)))
    out = img.copy()
    out.paste(rgb.convert("RGBA"), (0, 0))
    return out


def label_bar(width: int, text: str, height: int = 18) -> Image.Image:
    bar = Image.new("RGBA", (width, height), (0x1A, 0x1A, 0x22, 255))
    ImageDraw.Draw(bar).text((6, 4), text, fill=(0xE8, 0xC8, 0x5A, 255))
    return bar


# --------------------------------------------------------------------------- #
# tahap 2b: siklus siang/malam + cahaya api (meniru Lighting.cs)
# --------------------------------------------------------------------------- #
def night_amount(t: float) -> float:
    """Kurva malam yang sama dengan Lighting.Apply: pow(0.5 + 0.5 cos(2πt), 1.7)."""
    return (0.5 + 0.5 * math.cos(2 * math.pi * t)) ** 1.7


def apply_night(img: Image.Image, night: float) -> Image.Image:
    """CanvasModulate: lerp(putih, NightColor, night) dikalikan ke seluruh frame."""
    if night <= 0.001:
        return img
    tint = tuple(int(round((1.0 + (c - 1.0) * night) * 255)) for c in NIGHT_COLOR)
    rgb = ImageChops.multiply(img.convert("RGB"), Image.new("RGB", img.size, tint))
    return rgb.convert("RGBA")


def apply_lights(img: Image.Image, fires: list[tuple[float, float, float]], origin: tuple[int, int],
                 night: float, light_img: Image.Image) -> Image.Image:
    """PointLight2D (BlendMode Add): titik hangat beradius size*1.5 di tiap api."""
    ox, oy = origin
    base = img.convert("RGB")
    layer = Image.new("RGB", img.size, (0, 0, 0))
    energy = LIGHT_ENERGY * (0.3 + 0.7 * night)
    warm = tuple(c * 255 * energy for c in LIGHT_COLOR)
    alpha = light_img.getchannel("A")
    for fx, fy, size in fires:
        radius = max(8, int(size * 1.5))
        cx, cy = int(round(fx - ox)), int(round(fy - oy))
        if cx + radius < 0 or cy + radius < 0 or cx - radius > img.width or cy - radius > img.height:
            continue
        lum = alpha.resize((radius * 2, radius * 2))
        lp = lum.load()
        for y in range(radius * 2):
            ty = cy - radius + y
            if ty < 0 or ty >= img.height:
                continue
            for x in range(radius * 2):
                tx = cx - radius + x
                if tx < 0 or tx >= img.width:
                    continue
                v = lp[x, y]
                if v <= 0:
                    continue
                r, g, b = layer.getpixel((tx, ty))
                layer.putpixel((tx, ty), (
                    min(255, r + int(v * warm[0] / 255)),
                    min(255, g + int(v * warm[1] / 255)),
                    min(255, b + int(v * warm[2] / 255)),
                ))
    return ImageChops.add(base, layer).convert("RGBA")


# --- tahap 2c: api/bara/abu/tunas (samakan dengan game/scripts/Particles.cs) ---
TIME_OF_DAY_2C = 0.80              # senja: api terbaca, tanah masih terlihat
EMBER_PER_FIRE = 5
ASH_PER_ZONE = 26
SPROUTS_PER_ZONE = 6


def burnt_zones(data: dict) -> list[tuple[int, int, int, int]]:
    return [tuple(b["rect"]) for b in data["markers"].get("burnt", [])]


def draw_flames(img: Image.Image, fires: list[tuple[float, float, float]], origin: tuple[int, int],
                flame: Image.Image) -> Image.Image:
    """Nyala 4 frame di tiap api (frame dipilih deterministik; skala size/44)."""
    ox, oy = origin
    for i, (fx, fy, size) in enumerate(fires):
        frame = flame.crop(((i % 4) * 16, 0, (i % 4) * 16 + 16, 16))
        scale = max(1, int(round(size / 44.0)))
        if scale > 1:
            frame = frame.resize((16 * scale, 16 * scale), Image.NEAREST)
        img.alpha_composite(frame, (int(round(fx - ox)) - frame.width // 2,
                                    int(round(fy - oy)) - frame.height))
    return img


def draw_embers(img: Image.Image, fires: list[tuple[float, float, float]], origin: tuple[int, int],
                glow: Image.Image, rng: random.Random) -> Image.Image:
    """Bara kecil naik dari tiap api (pakai inti fx_glow_soft, diwarnai hangat)."""
    ox, oy = origin
    core = glow.crop((26, 26, 38, 38))
    warm_color = Image.new("RGB", (12, 12), (255, 190, 110))
    for fx, fy, size in fires:
        for _ in range(EMBER_PER_FIRE):
            ex = fx + rng.uniform(-8, 8)
            ey = fy - rng.uniform(2, 6 + size * 0.5)
            dot = ImageChops.multiply(core.convert("RGB"), warm_color).convert("RGBA")
            dot = dot.resize((rng.randint(2, 3), rng.randint(2, 3)), Image.NEAREST)
            alpha = dot.getchannel("A").point(lambda v: int(v * 0.85))
            dot.putalpha(alpha)
            img.alpha_composite(dot, (int(ex - ox), int(ey - oy)))
    return img


def draw_burnt_debris(img: Image.Image, zones: list[tuple[int, int, int, int]], origin: tuple[int, int],
                      sprout: Image.Image, rng: random.Random) -> Image.Image:
    """Abu beterbangan + tunas tumbuh di zona bekas terbakar yang terlihat."""
    ox, oy = origin
    for x0, y0, zw, zh in zones:
        px0, py0, px1, py1 = x0 * TILE, y0 * TILE, (x0 + zw) * TILE, (y0 + zh) * TILE
        if px1 <= ox or py1 <= oy or px0 >= ox + VIEW_W or py0 >= oy + VIEW_H:
            continue
        for _ in range(ASH_PER_ZONE):
            ax = rng.uniform(px0, px1)
            ay = rng.uniform(py0, py1)
            if not (ox <= ax < ox + VIEW_W and oy <= ay < oy + VIEW_H):
                continue
            f = rng.randint(1, 2)
            img.alpha_composite(Image.new("RGBA", (f, f), (0x9A, 0x96, 0x8E, rng.randint(120, 200))),
                                (int(ax - ox), int(ay - oy)))
        for _ in range(SPROUTS_PER_ZONE):
            sx = rng.uniform(px0, px1)
            sy = rng.uniform(py0, py1)
            if not (ox <= sx < ox + VIEW_W and oy <= sy < oy + VIEW_H):
                continue
            sp = sprout if rng.random() < 0.5 else sprout.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
            img.alpha_composite(sp, (int(sx - ox) - 4, int(sy - oy) - 8))
    return img


def load_hero() -> tuple[dict, Image.Image]:
    """Metadata frame hero + atlas (aset yang sama dengan game)."""
    meta = json.loads((CHAR_DIR / "hero.json").read_text())
    atlas = Image.open(CHAR_DIR / "hero_atlas.png").convert("RGBA")
    return meta, atlas


def paste_hero(out: Image.Image, meta: dict, atlas: Image.Image, frame: str,
               feet: tuple[float, float], flip: bool = False) -> None:
    """Tempel frame hero dengan garis kaki tepat di `feet` (anchor sama dengan game)."""
    fr = meta["frames"][frame]
    sprite = atlas.crop((fr["x"], fr["y"], fr["x"] + fr["w"], fr["y"] + fr["h"]))
    if flip:
        sprite = sprite.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    foot = meta["foot"]
    out.alpha_composite(sprite, (int(feet[0] - foot["x"]), int(feet[1] - foot["y"])))


def glow_tinted(glow: Image.Image, color: tuple[float, float, float], size: int,
                alpha: float) -> Image.Image:
    """Inti fx_glow_soft diwarnai & di-skala (partikel debu/percikan)."""
    sprite = glow.crop((24, 24, 40, 40)).resize((size, size), Image.LANCZOS)
    mask = sprite.getchannel("A").point(lambda a: int(a * alpha))
    tint = Image.new("RGBA", sprite.size, (int(color[0] * 255), int(color[1] * 255), int(color[2] * 255), 255))
    tint.putalpha(mask)
    return tint


def draw_dust_trail(out: Image.Image, feet: tuple[float, float], glow: Image.Image,
                    rng: random.Random) -> None:
    """Debu langkah: beberapa kepulan memudar di belakang kaki kiri."""
    puffs = ((-7, -1, 10, 0.65), (-15, -3, 13, 0.5), (-24, -6, 16, 0.34), (-34, -9, 19, 0.2))
    for dx, dy, size, alpha in puffs:
        jx = rng.uniform(-1.5, 1.5)
        puff = glow_tinted(glow, (0.76, 0.72, 0.64), size, alpha)
        out.alpha_composite(puff, (int(feet[0] + dx + jx - size / 2), int(feet[1] + dy - size / 2)))


def draw_sparks(out: Image.Image, center: tuple[float, float], glow: Image.Image,
                rng: random.Random, color: tuple[float, float, float] = (1.0, 0.85, 0.38),
                count: int = 9) -> None:
    """Percikan radial (peti dibuka / pukulan kena) — Burst tahap 2f."""
    for _ in range(count):
        angle = rng.uniform(0.15, math.pi - 0.15)
        dist = rng.uniform(3, 15)
        size = rng.choice((3, 4, 5))
        x = center[0] + math.cos(angle) * dist
        y = center[1] - math.sin(angle) * dist * 0.8
        spark = glow_tinted(glow, color, size, rng.uniform(0.7, 1.0))
        out.alpha_composite(spark, (int(x - size / 2), int(y - size / 2)))


def pick_feel_spot(data: dict, crop_box: tuple[int, int]) -> tuple[tuple[float, float], tuple[float, float] | None]:
    """Posisi kaki hero + pusat percikan (koordinat lokal crop): di samping peti."""
    ox, oy = crop_box
    for chest in data["markers"]["chests"]:
        px, py = chest["col"] * TILE - ox, chest["row"] * TILE - oy
        if 56 <= px <= VIEW_W - 40 and 48 <= py <= VIEW_H - 48:
            return (px - 10.0, py + 14.0), (px + 8.0, py - 2.0)
    return (VIEW_W / 2.0, VIEW_H / 2.0), None


def compose_2c(base: Image.Image, fires, crop_box, noise, light_img: Image.Image,
               flame_img: Image.Image, sprout_img: Image.Image, glow_img: Image.Image,
               zones: list[tuple[int, int, int, int]], seed: int = 20260219) -> Image.Image:
    """Adegan senja tahap 2c: malam + cahaya api + nyala/bara/abu/tunas + pipeline FX.

    Dipakai juga sebagai dasar tahap 2d (ground dirender ulang dengan scorch/ripple)
    dengan seed yang sama supaya partikelnya identik dan diff-nya fokus ke tile.
    """
    night = night_amount(TIME_OF_DAY_2C)
    rng = random.Random(seed)
    out = apply_night(base.copy(), night)
    out = apply_lights(out, fires, crop_box, night, light_img)
    out = draw_burnt_debris(out, zones, crop_box, sprout_img, rng)
    out = draw_flames(out, fires, crop_box, flame_img)
    out = draw_embers(out, fires, crop_box, glow_img, rng)
    out = apply_smoke(out, noise, crop_box)
    out = apply_heat(out, fires, crop_box, noise)
    out = apply_glow(out)
    return apply_grade(out, night)


def write_trio(stage: str, before: Image.Image, after: Image.Image,
               before_label: str, after_label: str) -> None:
    before_big = before.resize((VIEW_W * SCALE, VIEW_H * SCALE), Image.NEAREST)
    after_big = after.resize((VIEW_W * SCALE, VIEW_H * SCALE), Image.NEAREST)
    before_big.convert("RGB").save(DOCS / f"preview-stage-{stage}-before.png")
    after_big.convert("RGB").save(DOCS / f"preview-stage-{stage}-after.png")

    width = VIEW_W * SCALE
    compare = Image.new("RGBA", (width, (VIEW_H * SCALE + 18) * 2), (0x11, 0x11, 0x18, 255))
    compare.alpha_composite(label_bar(width, before_label), (0, 0))
    compare.alpha_composite(before_big, (0, 18))
    compare.alpha_composite(label_bar(width, after_label), (0, 18 + VIEW_H * SCALE))
    compare.alpha_composite(after_big, (0, 18 + VIEW_H * SCALE + 18))
    compare.convert("RGB").save(DOCS / f"preview-stage-{stage}-compare.png")
    print(f"docs/preview-stage-{stage}-before.png")
    print(f"docs/preview-stage-{stage}-after.png")
    print(f"docs/preview-stage-{stage}-compare.png")


def main() -> None:
    data, tileset, atlas = load_world()
    world_img = render_world(data, tileset, atlas)
    fires = heat_sources(data, tileset)
    crop_box = pick_crop(data, fires)
    noise_img = Image.open(FX_DIR / "fx_noise.png").convert("RGB")
    noise = [[noise_img.getpixel((x, y)) for x in range(noise_img.width)] for y in range(noise_img.height)]
    light_img = Image.open(FX_DIR / "fx_light_soft.png").convert("RGBA")

    base = world_img.crop((crop_box[0], crop_box[1], crop_box[0] + VIEW_W, crop_box[1] + VIEW_H))

    # tahap 2a: pipeline FX (asap -> heat haze -> bloom -> grading)
    after2a = base.copy()
    after2a = apply_smoke(after2a, noise, crop_box)
    after2a = apply_heat(after2a, fires, crop_box, noise)
    after2a = apply_glow(after2a)
    after2a = apply_grade(after2a)
    write_trio("2a", base, after2a,
               "SEBELUM - tanpa pipeline FX (tahap 2a)",
               "SESUDAH - asap + heat haze + bloom + grading")

    # tahap 2b: canvas modulate malam + cahaya api, lalu pipeline FX yang sama
    night = night_amount(TIME_OF_DAY)
    after2b = apply_night(base.copy(), night)
    after2b = apply_lights(after2b, fires, crop_box, night, light_img)
    after2b = apply_smoke(after2b, noise, crop_box)
    after2b = apply_heat(after2b, fires, crop_box, noise)
    after2b = apply_glow(after2b)
    after2b = apply_grade(after2b, night)
    write_trio("2b", after2a, after2b,
               "SEBELUM - siang, akhir tahap 2a",
               f"SESUDAH - malam (t={TIME_OF_DAY:0.2f}, night={night:0.2f}) + cahaya api")

    # tahap 2c: nyala 4-frame, bara, abu & tunas (dekoratif) di waktu senja
    zones = burnt_zones(data)
    flame_img = Image.open(FX_DIR / "fx_flame.png").convert("RGBA")
    sprout_img = Image.open(FX_DIR / "fx_sprout.png").convert("RGBA")
    glow_img = Image.open(FX_DIR / "fx_glow_soft.png").convert("RGBA")
    after2c = compose_2c(base, fires, crop_box, noise, light_img, flame_img, sprout_img, glow_img, zones)
    write_trio("2c", after2b, after2c,
               "SEBELUM - akhir tahap 2b (malam + cahaya api)",
               f"SESUDAH - + nyala 4-frame, bara, abu & tunas ({len(zones)} zona hangus)")

    # tahap 2d: tanah hangus di zona terbakar + riak air menjalar per sel.
    # Ground dirender ulang (scorch/ripple), efek senja 2c diulang dengan seed sama.
    world_img_2d = render_world(data, tileset, atlas, scorch=True, ripple=True)
    base2d = world_img_2d.crop((crop_box[0], crop_box[1], crop_box[0] + VIEW_W, crop_box[1] + VIEW_H))
    after2d = compose_2c(base2d, fires, crop_box, noise, light_img, flame_img, sprout_img, glow_img, zones)
    write_trio("2d", after2c, after2d,
               "SEBELUM - akhir tahap 2c (zona hangus polos, riak seragam)",
               "SESUDAH - + tanah hangus & riak air menjalar (tahap 2d)")

    # bukti riak per sel: zoom 6x di tepi sungai ramai, tanpa efek supaya tile terbaca jelas
    shore_x, shore_y = pick_water_shores(data)
    zoom_w, zoom_h = 160, 90
    zx = min(max(0, shore_x * TILE - zoom_w // 2), data["width"] * TILE - zoom_w)
    zy = min(max(0, shore_y * TILE - zoom_h // 2), data["height"] * TILE - zoom_h)
    flat_zoom = render_world(data, tileset, atlas).crop((zx, zy, zx + zoom_w, zy + zoom_h))
    waved_zoom = render_world(data, tileset, atlas, ripple=True).crop((zx, zy, zx + zoom_w, zy + zoom_h))
    flat_zoom = flat_zoom.resize((VIEW_W * SCALE, VIEW_H * SCALE), Image.NEAREST)
    waved_zoom = waved_zoom.resize((VIEW_W * SCALE, VIEW_H * SCALE), Image.NEAREST)
    write_trio("2d-water", flat_zoom, waved_zoom,
               "SEBELUM - riak seragam (semua sel fase yang sama)",
               "SESUDAH - riak per sel: tiap sel fase berbeda (zoom 6x)")

    # tahap 2f: game feel — debu langkah & percikan (mock dari aset hero asli;
    # hit-stop & shake tidak bisa dipotret statis, dijelaskan di README)
    feel_meta, feel_atlas = load_hero()
    feel_feet, feel_spark = pick_feel_spot(data, crop_box)
    rng_feel = random.Random(20260607)
    before2f = base.copy()
    paste_hero(before2f, feel_meta, feel_atlas, "right_run_2", feel_feet)
    after2f = base.copy()
    draw_dust_trail(after2f, feel_feet, glow_img, rng_feel)
    paste_hero(after2f, feel_meta, feel_atlas, "right_run_2", feel_feet)
    if feel_spark is not None:
        draw_sparks(after2f, feel_spark, glow_img, rng_feel)
    else:
        draw_sparks(after2f, (feel_feet[0] + 24, feel_feet[1] - 15), glow_img, rng_feel,
                    color=(1.0, 0.55, 0.34), count=6)
    write_trio("2f", before2f, after2f,
               "SEBELUM - akhir 2e: hero lari, kamera diam, tanpa debu",
               "SESUDAH - + debu langkah, percikan pukulan & shake (tahap 2f)")

    scorch_cells = sum(1 for y in range(data["height"]) for x in range(data["width"])
                       if scorched_variant(data, data["tile_names"][data["ground_tiles"][y][x]], x, y))
    print(f"stage 2a: crop {crop_box} ({VIEW_W}x{VIEW_H}), {len(fires)} titik api di peta")
    print(f"stage 2b: warna malam lerp {night:0.2f} ke {NIGHT_COLOR}, cahaya radius size*1.5")
    print(f"stage 2c: senja t={TIME_OF_DAY_2C}, {EMBER_PER_FIRE} bara/api, {len(zones)} zona terbakar")
    print(f"stage 2d: {scorch_cells} tile tanah hangus, riak air offset per sel (salt 91)")
    print(f"stage 2d-water: zoom tepi sungai di ({shore_x},{shore_y}) [{zx},{zy}] 6x")
    spark_note = "peti" if feel_spark is not None else "tanpa peti di crop"
    print(f"stage 2f: pose {feel_atlas.width}x{feel_atlas.height}, kaki di ({feel_feet[0]:.0f},{feel_feet[1]:.0f}), percikan: {spark_note}")


if __name__ == "__main__":
    main()
