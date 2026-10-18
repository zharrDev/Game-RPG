#!/usr/bin/env python3
"""Generator tileset 16x16 px untuk overworld RPG top-down (gaya SNES JRPG).

Menghasilkan seluruh aset environment yang diminta prompt:
  rumput + 4 variasi + bunga, jalan tanah (autotile 16 arah), jalan batu,
  lantai batu, air dangkal + tepi pantai, tebing/batu, pohon oak & pinus
  (2 ukuran), semak, batu besar, pagar & gerbang, dinding rumah, pintu,
  jendela, atap rumbia & sirap, cerobong, tangga, jembatan kayu, sumur,
  tong, peti kayu, papan penunjuk, peti harta, obor & lentera, gua, dsb.

Semua digambar prosedural dari palet yang sama dengan karakter Arga
(PROMPTS.md) supaya gaya, ketebalan outline, shading dan warnanya nyambung.
Tidak ada anti-aliasing, gradien, atau dithering noise.

Output:
  game/assets/tiles/tileset.png        atlas tile (16 kolom)
  game/assets/tiles/tileset.json       indeks: nama -> rect, ukuran, collision
  docs/preview-tileset.png             pratinjau 5x + grid (untuk dilihat manusia)

Jalankan:
    python3 tools/generate_tileset.py
"""

from __future__ import annotations

import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw

TILE = 16
COLS = 16
OUT_DIR = Path("game/assets/tiles")

# --------------------------------------------------------------------------- #
# palet: muted earthy, 3-4 shade per material, konsisten dengan PROMPTS.md
# --------------------------------------------------------------------------- #
OUTL = (0x20, 0x20, 0x2E, 255)

GRASS = [(0x3C, 0x5F, 0x26, 255), (0x4F, 0x7A, 0x33, 255), (0x5F, 0x8F, 0x3D, 255), (0x6F, 0xA3, 0x48, 255)]
GRASS_HI = (0x83, 0xB6, 0x57, 255)
DIRT = [(0x4E, 0x35, 0x1C, 255), (0x6B, 0x4A, 0x28, 255), (0x8A, 0x61, 0x36, 255), (0xA1, 0x73, 0x3F, 255)]
DIRT_HI = (0xB9, 0x8A, 0x4E, 255)
COBBLE = [(0x4A, 0x4E, 0x55, 255), (0x5C, 0x60, 0x67, 255), (0x76, 0x7B, 0x83, 255), (0x8F, 0x94, 0x9C, 255)]
COBBLE_HI = (0xAA, 0xB0, 0xB8, 255)
STONE = [(0x54, 0x5A, 0x62, 255), (0x6D, 0x73, 0x7B, 255), (0x86, 0x8D, 0x96, 255), (0xA0, 0xA7, 0xB0, 255)]
STONE_HI = (0xBC, 0xC3, 0xCB, 255)
WATER = [(0x1F, 0x3E, 0x60, 255), (0x2B, 0x4F, 0x77, 255), (0x37, 0x61, 0x8F, 255), (0x45, 0x74, 0xA6, 255)]
WATER_HI = (0x5B, 0x8F, 0xBB, 255)
FOAM = (0xCF, 0xE0, 0xE0, 255)
SAND = [(0x8A, 0x74, 0x4E, 255), (0xA8, 0x8F, 0x63, 255), (0xC4, 0xAA, 0x7C, 255), (0xDC, 0xC4, 0x99, 255)]
WOOD = [(0x5E, 0x3D, 0x20, 255), (0x7A, 0x4F, 0x2A, 255), (0x8A, 0x5A, 0x30, 255), (0xA9, 0x78, 0x45, 255)]
THATCH = [(0x7A, 0x5C, 0x2C, 255), (0x8A, 0x69, 0x35, 255), (0xA5, 0x81, 0x3F, 255), (0xC2, 0x9C, 0x5B, 255)]
SLATE = [(0x38, 0x40, 0x4C, 255), (0x45, 0x4F, 0x5C, 255), (0x5D, 0x6A, 0x78, 255), (0x76, 0x86, 0x96, 255)]
OAK = [(0x2C, 0x4A, 0x22, 255), (0x33, 0x55, 0x2A, 255), (0x4E, 0x7A, 0x35, 255), (0x66, 0x9A, 0x48, 255)]
PINE = [(0x1B, 0x3A, 0x2C, 255), (0x21, 0x44, 0x33, 255), (0x2A, 0x57, 0x41, 255), (0x35, 0x6B, 0x50, 255)]
CREAM = (0xEF, 0xE0, 0xC0, 255)
PLASTER = (0xDD, 0xCD, 0xAC, 255)
FLOWER = {
    "w": (0xF2, 0xF0, 0xE6, 255),
    "p": (0xD4, 0x78, 0x8F, 255),
    "b": (0x7F, 0x9F, 0xD4, 255),
    "y": (0xE8, 0xC8, 0x5A, 255),
}
GLASS = (0x4A, 0x6F, 0xA5, 255)
GLASS_HI = (0x6B, 0x93, 0xC9, 255)
FLAME = [(0xE8, 0xC8, 0x5A, 255), (0xE0, 0x8A, 0x3C, 255), (0xF5, 0xE7, 0xB0, 255)]
BURNT = [(0x2A, 0x26, 0x20, 255), (0x3A, 0x33, 0x2A, 255), (0x4C, 0x42, 0x34, 255), (0x8E, 0x88, 0x7C, 255)]
HOLE = (0x11, 0x11, 0x1A, 255)

CLEAR = (0, 0, 0, 0)


# --------------------------------------------------------------------------- #
# primitif gambar
# --------------------------------------------------------------------------- #
class Img:
    """Kanvas RGBA sederhana berbasis list piksel (tanpa operasi PIL langsung)."""

    def __init__(self, w: int, h: int):
        self.w, self.h = w, h
        self.px = [CLEAR] * (w * h)

    def get(self, x: int, y: int):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.px[y * self.w + x]
        return CLEAR

    def set(self, x: int, y: int, c) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y * self.w + x] = c

    def rect(self, x: int, y: int, w: int, h: int, c) -> None:
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.set(xx, yy, c)

    def hline(self, x: int, y: int, w: int, c) -> None:
        for xx in range(x, x + w):
            self.set(xx, y, c)

    def vline(self, x: int, y: int, h: int, c) -> None:
        for yy in range(y, y + h):
            self.set(x, yy, c)

    def ellipse(self, cx: float, cy: float, rx: float, ry: float, c, only_empty: bool = False) -> None:
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                if rx <= 0 or ry <= 0:
                    continue
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                    if only_empty and self.get(x, y)[3] != 0:
                        continue
                    self.set(x, y, c)

    def filled(self, x: int, y: int) -> bool:
        return self.get(x, y)[3] != 0

    def outline(self, c=OUTL, include_bounds: bool = True) -> None:
        """Tambah outline 1 px di luar siluet (tanpa menimpa piksel yang sudah ada)."""
        edges = []
        for y in range(self.h):
            for x in range(self.w):
                if self.filled(x, y):
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < self.w and 0 <= ny < self.h and self.filled(nx, ny):
                        edges.append((x, y))
                        break
        for x, y in edges:
            self.set(x, y, c)

    def speckle(self, rng: random.Random, colors, density: float, area=None, only_opaque=True, avoid=None) -> None:
        x0, y0, x1, y1 = area or (0, 0, self.w, self.h)
        for y in range(y0, y1):
            for x in range(x0, x1):
                if rng.random() > density:
                    continue
                if only_opaque and not self.filled(x, y):
                    continue
                cur = self.get(x, y)
                if avoid and cur in avoid:
                    continue
                self.set(x, y, rng.choice(colors))

    def to_image(self) -> Image.Image:
        im = Image.new("RGBA", (self.w, self.h))
        im.putdata(self.px)
        return im


# --------------------------------------------------------------------------- #
# registry tile
# --------------------------------------------------------------------------- #
TILES: list[dict] = []


def add(name, draw, w=1, h=1, solid=False, solid_cells=None, above=False, kind="ground",
        sort_y: int | None = None, base_row: int | None = None, tags=()) -> None:
    """Daftarkan satu tile (atau objek multi-tile). draw(img) menggambar di kanvas w*16 x h*16.

    base_row = baris yang garis alasnya dipakai untuk Y-sorting (default baris terbawah).
    """
    TILES.append(dict(name=name, draw=draw, w=w, h=h, solid=solid, solid_cells=solid_cells,
                      above=above, kind=kind, sort_y=sort_y,
                      base_row=base_row if base_row is not None else h - 1, tags=list(tags)))


def solid_all(w, h):
    return [(dx, dy) for dy in range(h) for dx in range(w)]


# --------------------------------------------------------------------------- #
# rumput & tanah
# --------------------------------------------------------------------------- #
def grass_base(img: Img, rng: random.Random, tones=GRASS, density=0.035, hi=GRASS_HI) -> None:
    img.rect(0, 0, img.w, img.h, tones[2])
    for y in range(img.h):
        for x in range(img.w):
            r = rng.random()
            if r < density:
                img.set(x, y, tones[1])
            elif r < density * 2:
                img.set(x, y, tones[3])
    img.speckle(rng, [tones[1]], 0.02)


def make_grass(seed: int):
    def draw(img: Img) -> None:
        grass_base(img, random.Random(seed))
    return draw


def make_scorched(seed: int):
    """Tanah hangus (dekoratif): arang gelap + abu; dipasang World sebagai overlay
    di zona bekas terbakar (markers.burnt) — walkable tetap dari tile aslinya."""

    def draw(img: Img) -> None:
        rng = random.Random(seed)
        img.rect(0, 0, img.w, img.h, BURNT[1])
        for y in range(img.h):
            for x in range(img.w):
                r = rng.random()
                if r < 0.18:
                    img.set(x, y, BURNT[0])
                elif r < 0.26:
                    img.set(x, y, BURNT[2])
        for _ in range(3):
            x, y = rng.randint(1, 12), rng.randint(1, 13)
            img.hline(x, y, rng.randint(2, 4), BURNT[0])
        img.speckle(rng, [BURNT[3]], 0.035)
        img.speckle(rng, [BURNT[2]], 0.04)

    return draw


def make_grass_tuft(seed: int):
    def draw(img: Img) -> None:
        rng = random.Random(seed)
        grass_base(img, rng)
        for _ in range(3):
            x, y = rng.randint(2, 12), rng.randint(4, 12)
            img.set(x, y, GRASS[0])
            img.set(x, y - 1, GRASS[3])
            img.set(x + 1, y - 1, GRASS[1])
            img.set(x - 1, y, GRASS[0])
    return draw


def make_grass_flowers(color, seed: int, count: int = 5):
    def draw(img: Img) -> None:
        rng = random.Random(seed)
        grass_base(img, rng)
        for _ in range(count):
            x, y = rng.randint(2, 13), rng.randint(3, 13)
            img.set(x, y, FLOWER[color])
            if rng.random() < 0.6:
                img.set(x, y - 1, FLOWER[color])
            img.set(x + 1, y, GRASS[0])
    return draw


def make_grass_dark():
    def draw(img: Img) -> None:
        rng = random.Random(7)
        grass_base(img, rng, tones=[GRASS[0], GRASS[1], GRASS[1], GRASS[2]], density=0.08)
    return draw


def make_grass_dirt_patch():
    def draw(img: Img) -> None:
        rng = random.Random(11)
        grass_base(img, rng)
        img.ellipse(8, 8, 5.5, 4.5, DIRT[2])
        img.ellipse(7, 7, 4.0, 3.0, DIRT[3])
        img.speckle(rng, [DIRT[1]], 0.25, (3, 4, 13, 12))
    return draw


def make_grass_rocks():
    def draw(img: Img) -> None:
        rng = random.Random(13)
        grass_base(img, rng)
        for cx, cy, r in ((5, 6, 2.2), (10, 9, 2.6), (8, 4, 1.6)):
            img.ellipse(cx, cy, r, r * 0.85, STONE[1])
            img.ellipse(cx - 0.6, cy - 0.6, r * 0.6, r * 0.5, STONE[2])
        img.outline(OUTL)
    return draw


def make_meadow_tall():
    def draw(img: Img) -> None:
        rng = random.Random(17)
        grass_base(img, rng, tones=[GRASS[1], GRASS[2], GRASS[2], GRASS[3]])
        for _ in range(9):
            x, y = rng.randint(1, 14), rng.randint(4, 13)
            img.vline(x, y - 3, 4, GRASS[3])
            img.set(x, y - 4, GRASS_HI)
    return draw


# --------------------------------------------------------------------------- #
# autotile: patch 4-bit (N=1,E=2,S=4,W=8) di atas rumput
# --------------------------------------------------------------------------- #
def patch_autotile(name_prefix, tones, kind="path", speckles=True, edge_shadow=True):
    """Buat 16 tile jalan/lantai dari bitmask tetangga yang sama jenisnya."""
    for mask in range(16):
        def draw(img: Img, mask=mask):
            rng = random.Random(100 + mask)
            grass_base(img, rng, density=0.03)
            mtop = 0 if mask & 1 else 2
            mright = 0 if mask & 2 else 2
            mbot = 0 if mask & 4 else 2
            mleft = 0 if mask & 8 else 2
            def ins(x, y, inside):
                # di luar kanvas: anggap "sejenis" kalau sisi itu punya tetangga sejenis
                if 0 <= x < 16 and 0 <= y < 16:
                    return inside[y][x]
                if y < 0:
                    return bool(mask & 1)
                if y > 15:
                    return bool(mask & 4)
                if x < 0:
                    return bool(mask & 8)
                return bool(mask & 2)

            inside = [[False] * 16 for _ in range(16)]
            for y in range(16):
                for x in range(16):
                    ok = (x >= mleft and x < 16 - mright and y >= mtop and y < 16 - mbot)
                    if ok:
                        for (mv, mv2, ex, ey) in ((mleft, mtop, 0, 0), (16 - mright, mtop, -1, 0),
                                                  (16 - mright, 16 - mbot, -1, -1), (mleft, 16 - mbot, 0, -1)):
                            if mv > 0 and mv2 > 0:
                                ox = mv + ex
                                oy = mv2 + ey
                                if (x - ox) * (1 if ex == 0 else -1) + (y - oy) * (1 if ey == 0 else -1) < 1:
                                    ok = False
                    inside[y][x] = ok
            for y in range(16):
                for x in range(16):
                    if inside[y][x]:
                        img.set(x, y, tones[2])
            if speckles:
                for y in range(16):
                    for x in range(16):
                        if not inside[y][x]:
                            continue
                        r = rng.random()
                        if r < 0.10:
                            img.set(x, y, tones[1])
                        elif r < 0.18:
                            img.set(x, y, tones[3])
            # tepi gelap di sisi dalam patch + bayangan lembut di rumput
            edges, shadows = [], []
            for y in range(16):
                for x in range(16):
                    if inside[y][x]:
                        if any(not ins(x + dx, y + dy, inside) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                            edges.append((x, y))
                    elif edge_shadow:
                        if any(ins(x + dx, y + dy, inside) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                            shadows.append((x, y))
            for x, y in edges:
                img.set(x, y, tones[0])
            for x, y in shadows:
                img.set(x, y, GRASS[1])
        add(f"{name_prefix}_{mask}", draw, kind=kind, tags=["autotile", name_prefix])


# --------------------------------------------------------------------------- #
# air (bitmask tetangga AIR) + variasi kedalaman
# --------------------------------------------------------------------------- #
def water_autotile():
    """Air autotile 16 arah + riak dua fase.

    Fase 0 = tile yang dipakai generate_world_map.py, fase 1 (``water_<mask>_b``)
    bentuk tepinya identik tapi riak/highlight bergeser, jadi game bisa
    menukar keduanya (World.SetWaterPhase) tanpa merusak garis pantai.
    """

    for mask in range(16):
        for phase in (0, 1):
            def draw(img: Img, mask=mask, phase=phase):
                rng = random.Random(200 + mask + phase * 97)
                img.rect(0, 0, 16, 16, WATER[2] if phase == 0 else WATER[1])
                for y in range(16):
                    for x in range(16):
                        r = rng.random()
                        if r < 0.10:
                            img.set(x, y, WATER[1] if phase == 0 else WATER[2])
                        elif r < 0.16:
                            img.set(x, y, WATER[3] if phase == 0 else WATER_HI)
                # tepi: busa kalau sisi itu bukan air (sama untuk kedua fase)
                if not mask & 1:
                    img.hline(0, 0, 16, WATER[3])
                    img.hline(0, 1, 16, FOAM)
                if not mask & 4:
                    img.hline(0, 15, 16, WATER[1])
                    img.hline(0, 14, 16, WATER_HI)
                if not mask & 8:
                    img.vline(0, 0, 16, WATER[3])
                    img.vline(1, 0, 16, FOAM)
                if not mask & 2:
                    img.vline(15, 0, 16, WATER[1])
                    img.vline(14, 0, 16, WATER_HI)
                # hilangkan busa di sudut yang bertemu daratan -> sudut rapi
                for cx, cy, dx, dy in ((0, 0, 1, 1), (15, 0, -1, 1), (0, 15, 1, -1), (15, 15, -1, -1)):
                    if not mask & (1 if cy == 0 else 4) and not mask & (8 if cx == 0 else 2):
                        img.set(cx, cy, WATER[0])
                        img.set(cx + dx, cy, WATER[0])
                        img.set(cx, cy + dy, WATER[0])

            name = f"water_{mask}" if phase == 0 else f"water_{mask}_b"
            tags = ["autotile", "water"] + (["animated"] if phase else [])
            add(name, draw, solid=True, kind="water", tags=tags)

    for i in range(3):
        def draw(img: Img, i=i):
            rng = random.Random(300 + i)
            img.rect(0, 0, 16, 16, WATER[1])
            for _ in range(3):
                x, y = rng.randint(1, 12), rng.randint(1, 13)
                img.hline(x, y, 3, WATER_HI)
                img.set(x + 3, y, WATER[3])
        add(f"water_deep_{i}", draw, solid=True, kind="water", tags=["animated", "water"])


# --------------------------------------------------------------------------- #
# tebing & batu
# --------------------------------------------------------------------------- #
def cliff_tiles():
    def face(img: Img, top_grass: bool, left: bool, right: bool, bottom: bool) -> None:
        rng = random.Random(400)
        img.rect(0, 0, 16, 16, STONE[1])
        for y in range(16):
            for x in range(16):
                r = rng.random()
                if r < 0.14:
                    img.set(x, y, STONE[0])
                elif r < 0.24:
                    img.set(x, y, STONE[2])
        for y in range(0, 16, 5):
            img.hline(1 if left else 0, y, 14 if left and right else 16, STONE[0])
        if top_grass:
            img.rect(0, 0, 16, 5, GRASS[2])
            img.hline(0, 0, 16, GRASS[3])
            img.hline(0, 4, 16, STONE[0])
            img.hline(0, 5, 16, STONE[2])
        if bottom:
            img.hline(0, 15, 16, STONE[0])
        if left:
            img.vline(0, 0, 16, STONE[0])
            img.vline(1, 0, 16, STONE[2])
        if right:
            img.vline(15, 0, 16, STONE[0])
            img.vline(14, 0, 16, STONE[1])

    add("cliff_top", lambda img: face(img, True, True, True, True), solid=True, kind="cliff")
    add("cliff_top_l", lambda img: face(img, True, True, False, True), solid=True, kind="cliff")
    add("cliff_top_r", lambda img: face(img, True, False, True, True), solid=True, kind="cliff")
    add("cliff_top_m", lambda img: face(img, True, False, False, True), solid=True, kind="cliff")
    add("cliff_face", lambda img: face(img, False, True, True, True), solid=True, kind="cliff")
    add("cliff_face_l", lambda img: face(img, False, True, False, True), solid=True, kind="cliff")
    add("cliff_face_r", lambda img: face(img, False, False, True, True), solid=True, kind="cliff")
    add("cliff_face_m", lambda img: face(img, False, False, False, True), solid=True, kind="cliff")

    def cliff_base(img: Img) -> None:
        face(img, False, True, True, False)
        img.hline(0, 13, 16, STONE[3])
        img.hline(0, 14, 16, STONE[2])
        rng = random.Random(401)
        for x in range(16):
            if rng.random() < 0.4:
                img.set(x, 15, STONE[1])
    add("cliff_base", cliff_base, solid=True, kind="cliff")

    def cave_interior(img: Img) -> None:
        img.rect(0, 0, 16, 16, HOLE)
        rng = random.Random(402)
        for _ in range(6):
            img.set(rng.randint(0, 15), rng.randint(0, 15), (0x1B, 0x1B, 0x26, 255))
    add("cave_floor", cave_interior, kind="cave")

    def cave_dark(img: Img) -> None:
        img.rect(0, 0, 16, 16, HOLE)
    add("cave_void", cave_dark, solid=True, kind="cave")

    def cave_wall(img: Img) -> None:
        rng = random.Random(403)
        img.rect(0, 0, 16, 16, (0x2A, 0x2A, 0x36, 255))
        for y in range(16):
            for x in range(16):
                r = rng.random()
                if r < 0.16:
                    img.set(x, y, (0x22, 0x22, 0x2C, 255))
                elif r < 0.26:
                    img.set(x, y, (0x36, 0x36, 0x44, 255))
        img.hline(0, 0, 16, (0x18, 0x18, 0x20, 255))
    add("cave_wall", cave_wall, solid=True, kind="cave")


# --------------------------------------------------------------------------- #
# pepohonan
# --------------------------------------------------------------------------- #
def tree_tiles():
    def canopy(img: Img, cx: float, cy: float, rx: float, ry: float, tones) -> None:
        img.ellipse(cx, cy, rx, ry, tones[1])
        img.ellipse(cx - rx * 0.25, cy - ry * 0.3, rx * 0.8, ry * 0.75, tones[2])
        img.ellipse(cx - rx * 0.4, cy - ry * 0.45, rx * 0.45, ry * 0.4, tones[3])
        img.ellipse(cx + rx * 0.45, cy + ry * 0.35, rx * 0.45, ry * 0.4, tones[0])

    def oak(seed: int, big: bool):
        def draw(img: Img) -> None:
            rng = random.Random(seed)
            s = img.w
            trunk_w = 4 if big else 3
            tx = s // 2 - trunk_w // 2
            img.rect(tx, s - 9 if big else s - 6, trunk_w, 9 if big else 6, WOOD[2])
            img.rect(tx, s - 9 if big else s - 6, 1, 9 if big else 6, WOOD[3])
            img.rect(tx + trunk_w - 1, s - 9 if big else s - 6, 1, 9 if big else 6, WOOD[0])
            if big:
                img.rect(tx - 2, s - 6, 2, 3, WOOD[1])
                img.rect(tx + trunk_w, s - 4, 2, 2, WOOD[1])
            canopy(img, s * 0.5, s * 0.42, s * 0.44, s * 0.36, OAK)
            rng_seed = rng.randint(0, 999)
            r2 = random.Random(rng_seed)
            for _ in range(int(s * 0.5)):
                x = r2.randint(int(s * 0.15), int(s * 0.85))
                y = r2.randint(int(s * 0.14), int(s * 0.7))
                if img.filled(x, y):
                    img.set(x, y, OAK[0] if r2.random() < 0.6 else OAK[3])
            img.outline(OUTL)
        return draw

    def pine(seed: int, big: bool):
        def draw(img: Img) -> None:
            rng = random.Random(seed)
            s = img.w
            trunk_w = 3
            tx = s // 2 - 1
            img.rect(tx, s - (8 if big else 5), trunk_w, 8 if big else 5, WOOD[1])
            img.rect(tx, s - (8 if big else 5), 1, 8 if big else 5, WOOD[2])
            tiers = 3 if big else 2
            for i in range(tiers):
                top = int(s * 0.06) + i * int(s * 0.2)
                half = int(s * 0.16) + i * int(s * 0.13)
                cy = top + int(s * 0.16)
                for y in range(top, cy + int(s * 0.1)):
                    t = (y - top) / max(1, (cy + int(s * 0.1) - top))
                    w = int(half * t) + 1
                    x0 = s // 2 - w
                    for x in range(x0, s // 2 + w):
                        img.set(x, y, PINE[2] if t > 0.4 else PINE[3])
                    img.set(x0, y, PINE[0])
                    img.set(s // 2 + w - 1, y, PINE[1])
                    img.set(x0 + 1, y, PINE[1])
            img.outline(OUTL)
        return draw

    add("oak_small", oak(501, False), kind="tree", above=True, solid=True, tags=["tree"])
    add("oak_large", oak(502, True), w=2, h=2, kind="tree", above=True,
        solid_cells=[(0, 1), (1, 1)], tags=["tree"])
    add("pine_small", pine(503, False), kind="tree", above=True, solid=True, tags=["tree"])
    add("pine_large", pine(504, True), w=2, h=2, kind="tree", above=True,
        solid_cells=[(0, 1), (1, 1)], tags=["tree"])

    def bush(seed: int, flower: str | None = None):
        def draw(img: Img) -> None:
            rng = random.Random(seed)
            img.ellipse(8, 10, 6.5, 4.6, OAK[1])
            img.ellipse(7, 8.5, 5.0, 3.6, OAK[2])
            img.ellipse(6, 7.5, 2.6, 2.0, OAK[3])
            for _ in range(6):
                x, y = rng.randint(3, 12), rng.randint(6, 13)
                if img.filled(x, y):
                    img.set(x, y, OAK[0])
            if flower:
                for _ in range(3):
                    x, y = rng.randint(4, 11), rng.randint(6, 12)
                    if img.filled(x, y):
                        img.set(x, y, FLOWER[flower])
            img.outline(OUTL)
        return draw

    add("bush_1", bush(511), kind="prop", above=True, solid=True, tags=["bush"])
    add("bush_2", bush(512), kind="prop", above=True, solid=True, tags=["bush"])
    add("bush_flowers", bush(513, "p"), kind="prop", above=True, solid=True, tags=["bush"])
    add("bush_berry", bush(514, "y"), kind="prop", above=True, solid=True, tags=["bush"])

    def boulder(seed: int, size: float):
        def draw(img: Img) -> None:
            rng = random.Random(seed)
            img.ellipse(8, 8, size, size * 0.9, STONE[1])
            img.ellipse(7, 6.6, size * 0.72, size * 0.55, STONE[2])
            img.ellipse(6, 6, size * 0.4, size * 0.3, STONE[3])
            for _ in range(4):
                x, y = rng.randint(int(8 - size), int(8 + size)), rng.randint(int(8 - size), int(8 + size))
                if img.filled(x, y):
                    img.set(x, y, STONE[0])
            img.outline(OUTL)
        return draw

    add("boulder_1", boulder(521, 6.2), kind="prop", above=True, solid=True, tags=["rock"])
    add("boulder_2", boulder(522, 5.0), kind="prop", above=True, solid=True, tags=["rock"])
    add("rock_small", boulder(523, 3.0), kind="prop", above=True, tags=["rock"])

    def pebbles(img: Img) -> None:
        rng = random.Random(524)
        grass_base(img, rng)
        for _ in range(5):
            x, y = rng.randint(2, 13), rng.randint(3, 13)
            img.set(x, y, STONE[2])
            img.set(x + 1, y, STONE[1])
            img.set(x, y + 1, STONE[1])
    add("pebbles", pebbles, kind="dressing", tags=["rock"])

    def stump(img: Img) -> None:
        img.ellipse(8, 9, 4.6, 3.6, WOOD[1])
        img.ellipse(8, 8, 4.6, 3.4, WOOD[2])
        img.ellipse(8, 8, 3.0, 2.0, WOOD[3])
        img.set(8, 8, WOOD[1])
        img.outline(OUTL)
    add("stump", stump, kind="prop", above=True, solid=True, tags=["wood"])

    def log(img: Img) -> None:
        img.rect(2, 8, 12, 5, WOOD[2])
        img.hline(2, 8, 12, WOOD[3])
        img.hline(2, 12, 12, WOOD[0])
        img.ellipse(3, 10.5, 2.0, 2.6, WOOD[1])
        img.ellipse(3, 10.5, 1.2, 1.6, WOOD[3])
        img.outline(OUTL)
    add("log", log, kind="prop", above=True, solid=True, tags=["wood"])


# --------------------------------------------------------------------------- #
# pagar, gerbang, penunjuk arah
# --------------------------------------------------------------------------- #
def fence_tiles():
    def fence_h(img: Img) -> None:
        img.rect(0, 7, 16, 2, WOOD[2])
        img.hline(0, 7, 16, WOOD[3])
        img.hline(0, 8, 16, WOOD[1])
        img.rect(2, 4, 2, 10, WOOD[1])
        img.vline(2, 4, 10, WOOD[3])
        img.hline(2, 13, 2, WOOD[0])
    add("fence_h", fence_h, solid=True, kind="prop", above=True, tags=["fence"])

    def fence_v(img: Img) -> None:
        img.rect(6, 0, 2, 16, WOOD[2])
        img.vline(6, 0, 16, WOOD[3])
        img.vline(7, 0, 16, WOOD[1])
        img.hline(4, 5, 6, WOOD[1])
        img.hline(4, 4, 6, WOOD[3])
        img.hline(4, 10, 6, WOOD[1])
        img.hline(4, 9, 6, WOOD[3])
    add("fence_v", fence_v, solid=True, kind="prop", above=True, tags=["fence"])

    def fence_post(img: Img) -> None:
        img.rect(6, 1, 3, 14, WOOD[1])
        img.vline(6, 1, 14, WOOD[3])
        img.vline(8, 1, 14, WOOD[0])
        img.rect(5, 1, 5, 2, WOOD[2])
    add("fence_post", fence_post, solid=True, kind="prop", above=True, tags=["fence"])

    def gate(img: Img, open_gate: bool) -> None:
        img.rect(6, 1, 3, 14, WOOD[1])
        img.vline(6, 1, 14, WOOD[3])
        img.rect(9, 1, 3, 14, WOOD[1])
        img.vline(9, 1, 14, WOOD[3])
        if open_gate:
            img.hline(11, 5, 4, WOOD[2])
            img.hline(11, 4, 4, WOOD[3])
        else:
            img.hline(8, 5, 3, WOOD[2])
            img.hline(8, 9, 3, WOOD[2])
    add("gate_closed", lambda img: gate(img, False), solid=True, kind="prop", above=True, tags=["fence"])
    add("gate_open", lambda img: gate(img, True), kind="prop", above=True, tags=["fence"])

    def signpost(img: Img) -> None:
        img.rect(7, 6, 2, 9, WOOD[1])
        img.rect(2, 2, 12, 6, WOOD[2])
        img.hline(2, 2, 12, WOOD[3])
        img.hline(2, 7, 12, WOOD[0])
        for i in range(3):
            img.hline(4, 3 + i * 2, 8 - i, SLATE[1] if i % 2 else CREAM)
        img.outline(OUTL)
    add("signpost", signpost, kind="prop", above=True, solid=True, tags=["detail"])

    def signpost_r(img: Img) -> None:
        img.rect(7, 6, 2, 9, WOOD[1])
        img.rect(9, 3, 6, 3, WOOD[2])
        img.hline(9, 3, 6, WOOD[3])
        img.hline(9, 5, 6, WOOD[0])
        img.outline(OUTL)
    add("signpost_r", signpost_r, kind="prop", above=True, tags=["detail"])


# --------------------------------------------------------------------------- #
# rumah: dinding, pintu, jendela, atap
# --------------------------------------------------------------------------- #
def house_tiles():
    def wall(img: Img, window: bool, stone: bool, door: bool = False) -> None:
        tones = STONE if stone else WOOD
        img.rect(0, 0, 16, 16, tones[2])
        for y in range(0, 16, 4):
            img.hline(0, y, 16, tones[0])
            img.hline(0, y + 1, 16, tones[3] if not stone else tones[2])
        if stone:
            for y in range(0, 16, 4):
                off = 0 if (y // 4) % 2 == 0 else 8
                img.vline(off, y, 4, tones[0])
                img.vline(off + 8, y, 4, tones[0])
                img.vline(off + 1, y, 4, tones[3])
        img.rect(0, 0, 16, 1, tones[0])
        img.rect(0, 15, 16, 1, tones[0])
        if window:
            img.rect(3, 5, 10, 8, WOOD[0])
            img.rect(4, 6, 8, 6, GLASS)
            img.hline(4, 6, 8, GLASS_HI)
            img.vline(7, 6, 6, WOOD[0])
            img.hline(4, 8, 8, WOOD[0])
            img.vline(6, 6, 6, GLASS_HI)
        if door:
            img.rect(3, 2, 10, 14, WOOD[0])
            img.rect(4, 3, 8, 13, WOOD[2])
            img.vline(8, 3, 13, WOOD[0])
            img.vline(6, 3, 13, WOOD[3])
            img.vline(10, 3, 13, WOOD[1])
            img.rect(9, 9, 2, 2, CREAM)
        img.outline(OUTL)

    add("wall_wood", lambda img: wall(img, False, False), solid=True, kind="wall")
    add("wall_wood_window", lambda img: wall(img, True, False), solid=True, kind="wall")
    add("wall_wood_door", lambda img: wall(img, False, False, True), solid=True, kind="wall")
    add("wall_stone", lambda img: wall(img, False, True), solid=True, kind="wall")
    add("wall_stone_window", lambda img: wall(img, True, True), solid=True, kind="wall")

    def plaster_wall(img: Img, window: bool) -> None:
        img.rect(0, 0, 16, 16, PLASTER)
        for x in (2, 13):
            img.vline(x, 0, 16, WOOD[1])
            img.vline(x + 1, 0, 16, WOOD[0])
        img.rect(0, 0, 16, 1, WOOD[1])
        img.rect(0, 15, 16, 1, WOOD[0])
        rng = random.Random(601)
        img.speckle(rng, [CREAM], 0.05, (3, 1, 13, 15))
        if window:
            img.rect(4, 5, 8, 7, WOOD[0])
            img.rect(5, 6, 6, 5, GLASS)
            img.hline(5, 6, 6, GLASS_HI)
            img.hline(5, 7, 5, WOOD[1])
        img.outline(OUTL)
    add("wall_plaster", lambda img: plaster_wall(img, False), solid=True, kind="wall")
    add("wall_plaster_window", lambda img: plaster_wall(img, True), solid=True, kind="wall")

    def roof(style, row: str, col: str, tone: int):
        def draw(img: Img) -> None:
            tones = THATCH if style == "thatch" else SLATE
            img.rect(0, 0, 16, 16, tones[tone])
            edge_l = col == "l"
            edge_r = col == "r"
            if row == "ridge":
                img.rect(2 if edge_l else 0, 0, 12 if edge_l and edge_r else (14 if edge_l else 16), 3, tones[3])
                img.hline(2 if edge_l else 0, 3, 14, tones[0])
                img.rect(0 if not edge_l else 2, 4, 16, 12, tones[tone])
            elif row == "mid":
                for y in range(0, 16, 5):
                    img.hline(0, y, 16, tones[0])
                    img.hline(0, y + 1, 16, tones[min(3, tone + 1)])
                img.hline(0, 15, 16, tones[0])
            else:  # eave
                for y in range(0, 12, 5):
                    img.hline(0, y, 16, tones[0])
                    img.hline(0, y + 1, 16, tones[min(3, tone + 1)])
                img.rect(0, 12, 16, 4, tones[0])
                img.hline(0, 12, 16, tones[2])
                for x in range(0, 16, 2):
                    img.set(x, 13, tones[2])
                    img.set(x, 14, tones[1])
            if style == "thatch":
                rng = random.Random(610 + tone)
                img.speckle(rng, [tones[0]], 0.06, only_opaque=True)
            if edge_l:
                img.vline(0, 0, 16, tones[0])
                img.vline(1, 0, 16, tones[1])
            if edge_r:
                img.vline(15, 0, 16, tones[0])
                img.vline(14, 0, 16, tones[1])
        return draw

    for style in ("thatch", "slate"):
        for row, tone in (("ridge", 3), ("mid", 2), ("eave", 1)):
            for col in ("l", "m", "r"):
                add(f"roof_{style}_{row}_{col}", roof(style, row, col, tone), kind="roof")
        add(f"roof_{style}_edge_l", roof(style, "mid", "l", 1), kind="roof")
        add(f"roof_{style}_edge_r", roof(style, "mid", "r", 1), kind="roof")

    def chimney(img: Img, top: bool) -> None:
        img.rect(5, 0, 6, 16, STONE[1])
        img.vline(5, 0, 16, STONE[0])
        img.vline(10, 0, 16, STONE[3])
        img.hline(6, 3, 4, STONE[0])
        img.hline(6, 9, 4, STONE[0])
        if top:
            img.rect(4, 0, 8, 3, STONE[2])
            img.hline(4, 0, 8, STONE[3])
            img.hline(4, 2, 8, STONE[0])
        img.outline(OUTL)
    add("chimney_top", lambda img: chimney(img, True), solid=True, kind="prop", above=True, tags=["house"])
    add("chimney_body", lambda img: chimney(img, False), solid=True, kind="prop", above=True, tags=["house"])

    def stair_stone(img: Img) -> None:
        for i in range(4):
            y = i * 4
            img.rect(0, y, 16, 4, STONE[1 + (i % 2)])
            img.hline(0, y, 16, STONE[3])
            img.hline(0, y + 3, 16, STONE[0])
    add("stair_stone", stair_stone, kind="stair", tags=["house"])

    def stair_wood(img: Img) -> None:
        for i in range(4):
            y = i * 4
            img.rect(0, y, 16, 4, WOOD[1 + (i % 2)])
            img.hline(0, y, 16, WOOD[3])
            img.hline(0, y + 3, 16, WOOD[0])
    add("stair_wood", stair_wood, kind="stair", tags=["house"])


# --------------------------------------------------------------------------- #
# jembatan, sumur, props
# --------------------------------------------------------------------------- #
def prop_tiles():
    def bridge_deck(img: Img, first: bool, last: bool) -> None:
        img.rect(0, 0, 16, 16, WOOD[1])
        for y in range(0, 16, 4):
            img.hline(0, y, 16, WOOD[0])
            img.hline(0, y + 1, 16, WOOD[3])
        if first:
            img.vline(0, 0, 16, WOOD[0])
            img.rect(0, 0, 3, 16, WOOD[2])
            img.hline(0, 0, 3, WOOD[3])
        if last:
            img.vline(15, 0, 16, WOOD[0])
            img.rect(13, 0, 3, 16, WOOD[2])
            img.hline(13, 0, 3, WOOD[3])
        img.hline(0, 0, 16, WOOD[3])
        img.hline(0, 15, 16, WOOD[0])
    add("bridge_deck_l", lambda img: bridge_deck(img, True, False), kind="bridge", tags=["bridge"])
    add("bridge_deck_m", lambda img: bridge_deck(img, False, False), kind="bridge", tags=["bridge"])
    add("bridge_deck_r", lambda img: bridge_deck(img, False, True), kind="bridge", tags=["bridge"])

    def bridge_rail(img: Img, first: bool, last: bool) -> None:
        img.rect(0, 8, 16, 3, WOOD[2])
        img.hline(0, 8, 16, WOOD[3])
        img.hline(0, 10, 16, WOOD[0])
        for x in range(1, 16, 7):
            img.rect(x, 5, 3, 11, WOOD[1])
            img.vline(x, 5, 11, WOOD[3])
            img.vline(x + 2, 5, 11, WOOD[0])
        if first:
            img.rect(0, 4, 3, 12, WOOD[1])
            img.vline(0, 4, 12, WOOD[3])
        if last:
            img.rect(13, 4, 3, 12, WOOD[1])
            img.vline(15, 4, 12, WOOD[0])
    add("bridge_rail_l", lambda img: bridge_rail(img, True, False), kind="bridge", tags=["bridge"])
    add("bridge_rail_m", lambda img: bridge_rail(img, False, False), kind="bridge", tags=["bridge"])
    add("bridge_rail_r", lambda img: bridge_rail(img, False, True), kind="bridge", tags=["bridge"])

    def bridge_v(img: Img, top: bool) -> None:
        img.rect(0, 0, 16, 16, WOOD[1])
        for x in range(0, 16, 4):
            img.vline(x, 0, 16, WOOD[0])
            img.vline(x + 1, 0, 16, WOOD[3])
        if top:
            img.hline(0, 0, 16, WOOD[0])
        img.vline(0, 0, 16, WOOD[0])
        img.vline(15, 0, 16, WOOD[0])
    add("bridge_v", lambda img: bridge_v(img, False), kind="bridge", tags=["bridge"])
    add("bridge_v_end", lambda img: bridge_v(img, True), kind="bridge", tags=["bridge"])

    def well():
        def draw(img: Img) -> None:
            # sumur 2x2 (32x32): dinding batu bulat + atap kayu + ember
            img.ellipse(16, 26, 11, 6, STONE[1])
            img.ellipse(16, 24, 11, 6, STONE[2])
            img.ellipse(16, 24, 7.5, 3.6, HOLE)
            img.ellipse(15, 23.4, 6.2, 2.6, (0x1B, 0x1B, 0x26, 255))
            img.rect(12, 8, 3, 14, WOOD[1])
            img.rect(18, 8, 3, 14, WOOD[1])
            img.vline(12, 8, 14, WOOD[3])
            img.vline(18, 8, 14, WOOD[3])
            for i in range(3):
                img.rect(8 - i, 4 + i * 2, 17 + i * 2, 2, THATCH[2 - i // 2])
            img.hline(7, 4, 19, THATCH[3])
            img.rect(15, 8, 2, 5, WOOD[0])
            img.rect(14, 12, 4, 4, WOOD[1])
            img.hline(14, 12, 4, WOOD[3])
            img.hline(14, 15, 4, WOOD[0])
            img.outline(OUTL)
        return draw
    add("well", well(), w=2, h=2, solid_cells=[(0, 1), (1, 1)], kind="prop", above=True, tags=["prop"])

    def barrel(img: Img) -> None:
        img.ellipse(8, 8, 5.4, 6.4, WOOD[2])
        img.vline(4, 8, 6, WOOD[1])
        img.hline(3, 4, 11, WOOD[3])
        img.hline(3, 11, 11, WOOD[0])
        img.ellipse(8, 3, 5.2, 2.0, WOOD[3])
        img.ellipse(8, 3, 3.0, 1.2, WOOD[1])
        img.rect(3, 6, 11, 1, SLATE[1])
        img.rect(3, 10, 11, 1, SLATE[1])
        img.outline(OUTL)
    add("barrel", barrel, kind="prop", above=True, solid=True, tags=["prop"])

    def crate(img: Img) -> None:
        img.rect(3, 3, 11, 11, WOOD[2])
        img.hline(3, 3, 11, WOOD[3])
        img.vline(3, 3, 11, WOOD[3])
        img.vline(13, 3, 11, WOOD[0])
        img.hline(3, 13, 11, WOOD[0])
        img.rect(3, 3, 11, 2, WOOD[3])
        img.rect(3, 12, 11, 2, WOOD[1])
        for i in range(1, 10):
            img.set(3 + i, 3 + i, WOOD[0])
        img.outline(OUTL)
    add("crate", crate, kind="prop", above=True, solid=True, tags=["prop"])

    def crate_stack(img: Img) -> None:
        img.rect(2, 5, 8, 10, WOOD[2])
        img.rect(6, 4, 8, 8, WOOD[3])
        img.hline(2, 5, 8, WOOD[3])
        img.hline(2, 14, 8, WOOD[0])
        for i in range(6):
            img.set(3 + i, 6 + i, WOOD[0])
        img.outline(OUTL)
    add("crate_stack", crate_stack, kind="prop", above=True, solid=True, tags=["prop"])

    def chest(img: Img, open_chest: bool) -> None:
        img.rect(3, 7, 11, 7, WOOD[2])
        img.rect(3 if not open_chest else 3, 3, 11, 5, WOOD[1])
        img.hline(3, 3, 11, WOOD[3])
        if open_chest:
            img.rect(4, 4, 9, 3, FLAME[0])
            img.rect(3, 3, 11, 2, WOOD[0])
        img.rect(3, 12, 11, 2, WOOD[0])
        img.rect(7, 6, 3, 4, SLATE[3])
        img.hline(7, 8, 3, SLATE[0])
        img.outline(OUTL)
    add("chest_closed", lambda img: chest(img, False), kind="prop", above=True, solid=True, tags=["prop"])
    add("chest_open", lambda img: chest(img, True), kind="prop", above=True, tags=["prop"])

    def lantern(img: Img) -> None:
        img.rect(7, 6, 2, 10, WOOD[1])
        img.vline(7, 6, 10, WOOD[3])
        img.rect(5, 1, 6, 6, SLATE[0])
        img.rect(6, 2, 4, 4, FLAME[2])
        img.rect(6, 3, 4, 2, FLAME[0])
        img.hline(5, 0, 6, SLATE[2])
        img.outline(OUTL)
    add("lantern", lantern, kind="prop", above=True, solid=True, tags=["light"])

    def torch(img: Img) -> None:
        img.rect(7, 8, 2, 8, WOOD[1])
        img.rect(6, 3, 4, 6, WOOD[2])
        img.hline(6, 3, 4, WOOD[3])
        img.rect(6, 0, 4, 4, FLAME[0])
        img.rect(7, 0, 2, 2, FLAME[2])
        img.outline(OUTL)
    add("torch", torch, kind="prop", above=True, solid=True, tags=["light"])

    def campfire(img: Img, phase: int) -> None:
        img.ellipse(8, 12, 6.0, 3.0, STONE[2])
        img.ellipse(8, 12, 4.4, 2.0, STONE[1])
        img.rect(4, 11, 8, 2, WOOD[0])
        img.rect(6, 9, 5, 2, WOOD[1])
        if phase >= 0:
            h = 7 if phase == 0 else 9
            img.rect(6, 12 - h, 4, h, FLAME[1])
            img.rect(7, 12 - h + 1, 2, h - 1, FLAME[0])
            img.rect(7, 12 - h + 2, 1, 3, FLAME[2])
    add("campfire_1", lambda img: campfire(img, 0), kind="prop", above=True, tags=["light", "animated"])
    add("campfire_2", lambda img: campfire(img, 1), kind="prop", above=True, tags=["light", "animated"])
    add("campfire_off", lambda img: campfire(img, -1), kind="prop", above=True, solid=True, tags=["prop"])

    def haystack(img: Img) -> None:
        img.ellipse(8, 9, 6.5, 5.6, THATCH[1])
        img.ellipse(7, 7.5, 5.2, 4.2, THATCH[2])
        img.ellipse(6.5, 6.5, 3.0, 2.4, THATCH[3])
        rng = random.Random(701)
        img.speckle(rng, [THATCH[0]], 0.08, only_opaque=True)
        img.outline(OUTL)
        img.rect(6, 14, 4, 2, WOOD[0])
    add("haystack", haystack, kind="prop", above=True, solid=True, tags=["prop"])

    def pot(img: Img) -> None:
        img.ellipse(8, 10, 4.2, 4.6, DIRT[2])
        img.ellipse(8, 7, 4.4, 1.8, DIRT[3])
        img.ellipse(8, 7, 3.0, 1.2, DIRT[0])
        img.outline(OUTL)
    add("pot", pot, kind="prop", above=True, solid=True, tags=["prop"])

    def market_stall(img: Img) -> None:
        img.rect(2, 6, 12, 2, CREAM)
        img.rect(2, 8, 12, 3, FLOWER["p"])
        for x in range(2, 14, 3):
            img.vline(x, 8, 3, CREAM)
        img.rect(3, 11, 2, 5, WOOD[1])
        img.rect(11, 11, 2, 5, WOOD[1])
        img.rect(3, 12, 10, 4, WOOD[2])
        img.hline(3, 12, 10, WOOD[3])
        img.outline(OUTL)
    add("market_stall", market_stall, kind="prop", above=True, solid=True, tags=["prop"])

    def cave_entrance(img: Img) -> None:
        # gua 2x2, diukir di dinding batu
        img.rect(0, 0, 32, 32, STONE[1])
        rng = random.Random(801)
        for y in range(32):
            for x in range(32):
                r = rng.random()
                if r < 0.12:
                    img.set(x, y, STONE[0])
                elif r < 0.22:
                    img.set(x, y, STONE[2])
        img.ellipse(16, 22, 11, 13, STONE[0])
        img.ellipse(16, 22, 9, 11, HOLE)
        img.ellipse(15, 24, 7, 8, (0x0C, 0x0C, 0x12, 255))
        for x in range(4, 28):
            if (x + 2) % 4 == 0:
                img.set(x, 9, STONE[3])
        for i in range(4):
            img.hline(0, 8 + i * 6, 32, STONE[0])
        img.outline(OUTL)
    add("cave_entrance", cave_entrance, w=2, h=2, solid_cells=[(0, 0), (1, 0)], kind="prop",
        above=True, tags=["cave"])


# --------------------------------------------------------------------------- #
# susun atlas
# --------------------------------------------------------------------------- #
def build() -> None:
    # penempatan tile: objek multi-tile tidak boleh menyeberang tepi/ menimpa tile lain
    occupied: set[tuple[int, int]] = set()
    cursor = [0, 0]
    placed: list[tuple[dict, int, int]] = []

    def place(w: int, h: int) -> tuple[int, int]:
        col, row = cursor
        while True:
            if col + w > COLS:
                col, row = 0, row + 1
                continue
            cells = [(col + dx, row + dy) for dy in range(h) for dx in range(w)]
            if all(c not in occupied for c in cells):
                occupied.update(cells)
                cursor[0], cursor[1] = col + w, row
                if cursor[0] >= COLS:
                    cursor[0], cursor[1] = 0, row + 1
                return col, row
            col += 1

    for t in TILES:
        placed.append((t, *place(t["w"], t["h"])))

    rows = max([r + t["h"] for t, _c, r in placed] + [1])
    atlas = Image.new("RGBA", (COLS * TILE, rows * TILE), CLEAR)
    index: dict[str, dict] = {}
    cells: list[dict] = []
    groups: dict[str, list[str]] = {}

    for t, col, row in placed:
        canvas = Img(t["w"] * TILE, t["h"] * TILE)
        t["draw"](canvas)
        canvas.outline(OUTL)  # outline dulu, baru di-crop: kanvas = ukuran objek persis
        sub = canvas.to_image()
        atlas.paste(sub, (col * TILE, row * TILE))
        index[t["name"]] = {
            "x": col * TILE, "y": row * TILE, "col": col, "row": row,
            "w": t["w"], "h": t["h"],
            "solid": bool(t["solid"] or t["solid_cells"]),
            "solid_cells": t["solid_cells"] if t["solid_cells"] is not None else (solid_all(t["w"], t["h"]) if t["solid"] else []),
            "above": bool(t["above"]),
            "base_row": t["base_row"],
            "sort_y": t["sort_y"] if t["sort_y"] is not None else (t["base_row"] + 1) * TILE,
            "kind": t["kind"],
            "tags": t["tags"],
        }
        # data per-cell: dipakai engine untuk membangun TileSet + collision + y-sort
        solid_set = set(t["solid_cells"]) if t["solid_cells"] is not None else (
            set(solid_all(t["w"], t["h"])) if t["solid"] else set())
        for dy in range(t["h"]):
            for dx in range(t["w"]):
                cells.append({
                    "name": f'{t["name"]}__{dx}_{dy}',
                    "group": t["name"],
                    "dx": dx, "dy": dy,
                    "col": col + dx, "row": row + dy,
                    "rect": [(col + dx) * TILE, (row + dy) * TILE, TILE, TILE],
                    "solid": (dx, dy) in solid_set,
                    "sort_y": (t["base_row"] + 1 - dy) * TILE,
                    "above": bool(t["above"]),
                    "kind": t["kind"],
                    "tags": t["tags"],
                })
        groups.setdefault(t["kind"], []).append(t["name"])

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    atlas.save(OUT_DIR / "tileset.png")
    meta = {
        "tile_size": TILE, "columns": COLS, "rows": rows,
        "tile_count": len(TILES),
        "cell_count": len(cells),
        "source": "tools/generate_tileset.py",
        "palette_note": "muted earthy, palet sama dengan karakter Arga (PROMPTS.md)",
        "tiles": index,
        "cells": cells,
    }
    (OUT_DIR / "tileset.json").write_text(json.dumps(meta, indent=2) + "\n")

    scale = 5
    preview = Image.new("RGBA", (atlas.width * scale, atlas.height * scale), (0x2A, 0x2A, 0x33, 255))
    big = atlas.resize((atlas.width * scale, atlas.height * scale), Image.NEAREST)
    preview.alpha_composite(big)
    d = ImageDraw.Draw(preview)
    for c in range(COLS + 1):
        d.line([(c * TILE * scale, 0), (c * TILE * scale, preview.height)], fill=(0xFF, 0x00, 0xFF, 90))
    for r in range(rows + 1):
        d.line([(0, r * TILE * scale), (preview.width, r * TILE * scale)], fill=(0xFF, 0x00, 0xFF, 90))
    docs = Path("docs")
    docs.mkdir(parents=True, exist_ok=True)
    preview.convert("RGB").save(docs / "preview-tileset.png")

    print(f"tileset.png  : {atlas.width}x{atlas.height} ({len(TILES)} tile, {len(cells)} cell, {rows} baris)")
    for kind, names in sorted(groups.items()):
        print(f"  {kind:10s} {len(names):3d} tile")


def main() -> None:
    grass_autotile_skip = True  # noqa: F841  (dokumentasi: rumput tidak pakai autotile)
    for i in range(4):
        add(f"grass_{i + 1}", make_grass(i + 1), tags=["grass"])
    add("grass_flowers_w", make_grass_flowers("w", 21), tags=["grass", "flower"])
    add("grass_flowers_p", make_grass_flowers("p", 22), tags=["grass", "flower"])
    add("grass_flowers_b", make_grass_flowers("b", 23), tags=["grass", "flower"])
    add("grass_flowers_y", make_grass_flowers("y", 24), tags=["grass", "flower"])
    add("grass_tuft", make_grass_tuft(31), tags=["grass"])
    add("grass_dark", make_grass_dark(), tags=["grass"])
    add("grass_dirt_patch", make_grass_dirt_patch(), tags=["grass"])
    add("grass_rocks", make_grass_rocks(), tags=["grass"])
    add("meadow_tall", make_meadow_tall(), tags=["grass"])
    def mixed_flowers(img: Img) -> None:
        rng = random.Random(26)
        grass_base(img, rng)
        for i, c in enumerate(("w", "p", "b", "y", "w", "y")):
            x, y = 3 + (i * 5) % 11, 4 + (i * 7) % 9
            img.set(x, y, FLOWER[c])
            if rng.random() < 0.5:
                img.set(x, y - 1, FLOWER[c])
            img.set(x + 1, y, GRASS[0])
    add("grass_flowers_mixed", mixed_flowers, tags=["grass", "flower"])
    for i in range(3):
        add(f"scorched_{i + 1}", make_scorched(910 + i), tags=["ground", "scorched"])

    patch_autotile("dirt", DIRT, kind="path")
    patch_autotile("cobble", COBBLE, kind="road")
    patch_autotile("sand", SAND, kind="sand")

    for i in range(4):
        add(f"stone_floor_{i + 1}", lambda img, i=i: (
            img.rect(0, 0, 16, 16, STONE[1]),
            img.speckle(random.Random(60 + i), [STONE[0], STONE[2]], 0.12),
            img.hline(0, 0, 16, STONE[0]),
            img.vline(0, 0, 16, STONE[0]),
        ), kind="floor", tags=["floor"])
    add("stone_floor_cracked", lambda img: (
        img.rect(0, 0, 16, 16, STONE[1]),
        img.speckle(random.Random(66), [STONE[0], STONE[2]], 0.14),
        img.hline(0, 0, 16, STONE[0]),
    ), kind="floor", tags=["floor"])

    water_autotile()
    cliff_tiles()
    tree_tiles()
    fence_tiles()
    house_tiles()
    prop_tiles()
    build()


if __name__ == "__main__":
    main()
