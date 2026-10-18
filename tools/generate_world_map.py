#!/usr/bin/env python3
"""Generator overworld map 16x16 tile untuk RPG top-down (gaya SNES JRPG).

Isi peta (sesuai brief prompt):
  · Desa di TENGAH: rumah kayu & batu, atap rumbia/sirap, sumur, pasar,
    jalan tanah dengan tambalan cobblestone, pagar, tong, peti.
  · Sungai berkelok dengan JEMBATAN KAYU di TIMUR.
  · HUTAN lebat (oak + pinus) di UTARA.
  · RERUNTUHAN batu kuno + MULUT GUA di BARAT LAUT.
  · Padang rumput dengan bunga, semak, batu, papan penunjuk arah, api unggun.

Output:
  game/assets/world/overworld.json       data peta: tile ground, props, marker, walkable
  game/assets/world/overworld_ground.png layer tanah saja (cadangan)
  docs/preview-world.png                 render peta penuh (untuk ditinjau manusia)

Jalankan:
    python3 tools/generate_world_map.py
"""

from __future__ import annotations

import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw

TILE = 16
W, H = 96, 72
ASSET_DIR = Path("game/assets")
TILES_DIR = ASSET_DIR / "tiles"
WORLD_DIR = ASSET_DIR / "world"

# terrain id
GRASS, MEADOW, DIRT, COBBLE, SAND, WATER, STONE, ROCK, CAVE, BRIDGE = range(10)
TERRAIN_NAMES = ["grass", "meadow", "dirt", "cobble", "sand", "water", "stone", "rock", "cave", "bridge"]

# terrain yang solid (menghalangi jalan) tanpa melihat tile
SOLID_TERRAIN = {WATER, ROCK}
# keluarga permukaan yang dianggap "sama" saat menghitung autotile
PATH_FAMILY = {DIRT, COBBLE, SAND}
WALK_TERRAIN = {GRASS, MEADOW, DIRT, COBBLE, SAND, STONE, CAVE, BRIDGE}

TILESET_PATH = TILES_DIR / "tileset.json"
tileset: dict = json.loads(TILESET_PATH.read_text())

g = [[GRASS] * W for _ in range(H)]
props: list[dict] = []
prop_occ: set[tuple[int, int]] = set()
rng = random.Random(20261016)


def hash2(x: int, y: int, salt: int = 0) -> float:
    """Nilai pseudo-acak deterministik 0..1 untuk koordinat tile."""
    h = (x * 374761393 + y * 668265263 + salt * 2654435761) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def in_bounds(x: int, y: int) -> bool:
    return 0 <= x < W and 0 <= y < H


def fill(x0: int, y0: int, w: int, h: int, t: int) -> None:
    for y in range(max(0, y0), min(H, y0 + h)):
        for x in range(max(0, x0), min(W, x0 + w)):
            g[y][x] = t


def rect_outline(x0, y0, w, h, t, thickness=1) -> None:
    for k in range(thickness):
        for x in range(x0, x0 + w):
            if in_bounds(x, y0 + k):
                g[y0 + k][x] = t
            if in_bounds(x, y0 + h - 1 - k):
                g[y0 + h - 1 - k][x] = t
        for y in range(y0, y0 + h):
            if in_bounds(x0 + k, y):
                g[y][x0 + k] = t
            if in_bounds(x0 + w - 1 - k, y):
                g[y][x0 + w - 1 - k] = t


def path(points: list[tuple[int, int]], width: int, t: int, wobble: int = 0) -> None:
    """Corak jalan ortogonal (L) antar waypoint, lebar `width`."""
    for i in range(len(points) - 1):
        (x0, y0), (x1, y1) = points[i], points[i + 1]
        steps = 0
        x, y = x0, y0
        while (x, y) != (x1, y1):
            off = int(round(math.sin(steps * 0.6) * wobble))
            for dx in range(-(width // 2), width - (width // 2)):
                for dy in range(-(width // 2), width - (width // 2)):
                    if not in_bounds(x + dx + off, y + dy):
                        continue
                    cur = g[y + dy][x + dx + off]
                    if cur in (WATER, ROCK, CAVE, BRIDGE):
                        continue  # jangan menembus sungai/tebing/gua/jembatan
                    g[y + dy][x + dx + off] = t
            if x != x1:
                x += 1 if x1 > x else -1
            elif y != y1:
                y += 1 if y1 > y else -1
            steps += 1


def add_prop(group: str, col: int, row: int, force: bool = False) -> bool:
    t = tileset["tiles"].get(group)
    if not t:
        raise KeyError(f"tile/objek tidak ada di tileset: {group}")
    w, h = t["w"], t["h"]
    cells = [(col + dx, row + dy) for dy in range(h) for dx in range(w)]
    if not force:
        for (cx, cy) in cells:
            if not in_bounds(cx, cy) or (cx, cy) in prop_occ or g[cy][cx] in SOLID_TERRAIN:
                return False
    for (cx, cy) in cells:
        if in_bounds(cx, cy):
            prop_occ.add((cx, cy))
    props.append({"group": group, "col": col, "row": row})
    return True


def place_house(col: int, row: int, w: int, style: str = "thatch", stone: bool = False) -> bool:
    """Rumah: 3 baris atap (ridge/mid/eave) + 1 baris dinding, lebar w tile."""
    for dx in range(w):
        for dy in range(4):
            if not in_bounds(col + dx, row + dy) or (col + dx, row + dy) in prop_occ:
                return False
    for dx in range(w):
        key = "l" if dx == 0 else ("r" if dx == w - 1 else "m")
        t = tileset["tiles"]
        for i, r in enumerate(("ridge", "mid", "eave")):
            name = f"roof_{style}_{r}_{key}"
            if name not in t:
                name = f"roof_{style}_{r}_m"
            add_prop(name, col + dx, row + i, force=True)
        wall = "wall_stone" if stone else "wall_wood"
        if dx == w // 2:
            wall = "wall_wood_door"
        elif dx in (1, w - 2):
            wall = "wall_stone_window" if stone else "wall_wood_window"
        add_prop(wall, col + dx, row + 3, force=True)
    if w >= 4 and rng.random() < 0.6:
        add_prop("chimney_top", col + 1, row - 1)
        add_prop("chimney_body", col + 1, row)
    return True


# --------------------------------------------------------------------------- #
# 1. rumput & padang rumput (variasi halus, bukan noise kasar)
# --------------------------------------------------------------------------- #
for y in range(H):
    for x in range(W):
        n = (hash2(x // 6, y // 6) * 0.6 + hash2(x // 3, y // 3, 7) * 0.4)
        g[y][x] = MEADOW if n > 0.68 else GRASS

# --------------------------------------------------------------------------- #
# 2. sungai berkelok di TIMUR + tepi pasir
# --------------------------------------------------------------------------- #
river_span: dict[int, tuple[int, int]] = {}
for y in range(H):
    cx = 78 + int(round(3.0 * math.sin(y * 0.085) + 2.0 * math.sin(y * 0.21 + 1.3)))
    half = 2 if y % 9 in (0, 1) else 3
    x0, x1 = cx - half, cx + half
    river_span[y] = (x0, x1)
    for x in range(max(0, x0 - 1), min(W, x1 + 2)):
        g[y][x] = SAND if x < x0 or x > x1 else WATER

# --------------------------------------------------------------------------- #
# 3. RERUNTUHAN + GUA di BARAT LAUT (plato batu dengan bukaan di selatan)
# --------------------------------------------------------------------------- #
RX, RY, RW, RH = 5, 7, 22, 19
rect_outline(RX, RY, RW, RH, ROCK, thickness=2)
fill(RX + 2, RY + 2, RW - 4, RH - 4, STONE)
# bukaan masuk dari selatan (tangga)
opening_x = RX + 9
for x in range(opening_x, opening_x + 3):
    for y in range(RY + RH - 2, RY + RH + 1):
        if in_bounds(x, y):
            g[y][x] = STONE
for x in range(opening_x, opening_x + 3):
    add_prop("stair_stone", x, RY + RH + 1)

# halaman dalam: paving rusak + pilar + dinding pecah
for i in range(14):
    px = rng.randint(RX + 3, RX + RW - 4)
    py = rng.randint(RY + 3, RY + RH - 4)
    if (px, py) not in prop_occ:
        add_prop(rng.choice(["boulder_2", "rock_small", "boulder_1", "stump"]), px, py)
for i in range(6):
    px = rng.randint(RX + 3, RX + RW - 5)
    py = rng.randint(RY + 4, RY + RH - 5)
    add_prop("cliff_top_m", px, py)  # pilar batu runtuh
# mulut gua di tebing utara plato
add_prop("cave_entrance", RX + 8, RY, force=True)
for x in range(RX + 8, RX + 10):
    g[RY][x] = ROCK
    g[RY + 1][x] = CAVE
for x in range(RX + 8, RX + 10):
    for y in range(RY + 2, RY + 5):
        g[y][x] = CAVE
add_prop("torch", RX + 6, RY + 3)
add_prop("torch", RX + 12, RY + 3)
add_prop("chest_closed", RX + 11, RY + 5)
add_prop("campfire_1", RX + 7, RY + 6, force=True)
add_prop("boulder_1", RX + 4, RY + 8)
add_prop("boulder_2", RX + 16, RY + 9)
add_prop("signpost", opening_x + 3, RY + RH + 2)

RUINS_PVE = (RX + 4, RY + 6)  # titik pusat halaman reruntuhan

# --------------------------------------------------------------------------- #
# 4. DESA di TENGAH
# --------------------------------------------------------------------------- #
VX, VY = 46, 30           # pojok kiri-atas kawasan desa
plaza = (VX + 2, VY + 6, 13, 8)
fill(plaza[0], plaza[1], plaza[2], plaza[3], COBBLE)          # alun-alun batu
fill(plaza[0] + 3, plaza[1] + 2, plaza[2] - 6, plaza[3] - 4, DIRT)  # tambalan tanah

# sumur di tengah alun-alun
well_x, well_y = plaza[0] + 5, plaza[1] + 3
add_prop("well", well_x, well_y, force=True)

# rumah-rumah mengelilingi alun-alun
houses = [
    (plaza[0] - 1, plaza[1] - 4, 5, "thatch", False),
    (plaza[0] + 5, plaza[1] - 4, 6, "slate", False),
    (plaza[0] + 12, plaza[1] - 3, 5, "thatch", True),
    (plaza[0] - 4, plaza[1] + 3, 5, "slate", True),
    (plaza[0] + 15, plaza[1] + 4, 5, "thatch", False),
]
for (hx, hy, hw, style, stone) in houses:
    place_house(hx, hy, hw, style, stone)

# pasar + barang di sekitar alun-alun
add_prop("market_stall", plaza[0] + 1, plaza[1] + 1)
add_prop("market_stall", plaza[0] + 1, plaza[1] + 4)
add_prop("crate_stack", plaza[0] + 3, plaza[1] + 6)
add_prop("crate", plaza[0] + 4, plaza[1] + 7)
add_prop("barrel", plaza[0] + 9, plaza[1] + 1)
add_prop("barrel", plaza[0] + 10, plaza[1] + 1)
add_prop("barrel", plaza[0] + 11, plaza[1] + 2)
add_prop("pot", plaza[0] + 2, plaza[1] + 6)
add_prop("lantern", plaza[0] - 1, plaza[1] + 1)
add_prop("lantern", plaza[0] + plaza[2], plaza[1] + plaza[3] - 2)
add_prop("campfire_1", plaza[0] + plaza[2] - 2, plaza[1] + plaza[3] - 1, force=True)
add_prop("haystack", plaza[0] + 16, plaza[1] + 7)

# pagar & sawah kecil di barat daya desa
for i in range(8):
    add_prop("fence_h", VX - 6 + i, VY + 16)
for i in range(4):
    add_prop("fence_v", VX - 6, VY + 13 + i)
add_prop("gate_open", VX + 2, VY + 16)
add_prop("fence_h", VX + 3, VY + 16)

# --------------------------------------------------------------------------- #
# 5. JALAN: desa <-> hutan (utara), <-> jembatan (timur), <-> padang (selatan),
#    <-> reruntuhan (barat)
# --------------------------------------------------------------------------- #
cross = (plaza[0] + 6, plaza[1] + 4)          # pertigaan utama
bridge_y = 46
path([cross, (cross[0], 21), (cross[0] - 5, 13)], 2, DIRT)                      # utara -> hutan
path([cross, (opening_x + 1, cross[1]), (opening_x + 1, RY + RH + 2)], 2, DIRT)  # barat -> reruntuhan
path([cross, (cross[0] + 4, cross[1] + 8), (cross[0] + 4, bridge_y)], 2, DIRT)   # tenggara
path([(cross[0] + 4, bridge_y), (river_span[bridge_y][0] - 2, bridge_y)], 2, DIRT)
path([cross, (cross[0], cross[1] + 18)], 2, DIRT)                                # selatan -> padang
path([(cross[0] + 4, cross[1] + 8), (plaza[0] + 18, cross[1] + 12)], 2, DIRT)    # jalan kecil timur
# jembatan kayu melintasi sungai
bx0, bx1 = river_span[bridge_y]
for x in range(bx0, bx1 + 1):
    g[bridge_y][x] = BRIDGE
for x in range(bx0 - 1, bx1 + 2):
    if in_bounds(x, bridge_y - 1):
        add_prop("bridge_rail_m", x, bridge_y - 1, force=(g[bridge_y - 1][x] == WATER))
    if in_bounds(x, bridge_y + 1):
        add_prop("bridge_rail_m", x, bridge_y + 1, force=(g[bridge_y + 1][x] == WATER))
add_prop("campfire_1", bx1 + 3, bridge_y - 1, force=True)   # checkpoint di ujung timur jembatan
add_prop("lantern", bx1 + 4, bridge_y - 2)
add_prop("lantern", bx0 - 2, bridge_y + 2)

# jalan menyusuri tepi sungai (menghubungkan jembatan ke hutan di utara)
path([(river_span[bridge_y][1] + 2, bridge_y - 4), (river_span[28][1] + 2, 28), (river_span[14][1] + 2, 14)], 2, DIRT)

# tambalan cobblestone (jalan lama) supaya jalan tanah tidak monoton
for y in range(H):
    for x in range(W):
        if g[y][x] == DIRT and hash2(x, y, 3) < 0.22:
            g[y][x] = COBBLE

# --------------------------------------------------------------------------- #
# 6. HUTAN di UTARA (oak + pinus, dua ukuran) — lebat tapi ada jalan & lapangan
# --------------------------------------------------------------------------- #
forest_rect = (0, 0, W, 24)
fx0, fy0, fw, fh = forest_rect
for y in range(fy0, min(H, fy0 + fh)):
    for x in range(fx0, min(W, fx0 + fw)):
        if x >= RX - 1 and x < RX + RW + 1 and y < RY + RH + 2:
            continue  # jangan menutup reruntuhan
        if g[y][x] not in (GRASS, MEADOW):
            continue
        # koridor: tepi jalan (jarak ke tile DIRT/COBBLE terdekat <= 2) dibiarkan terbuka
        near_path = any(
            0 <= x + dx < W and 0 <= y + dy < H and g[y + dy][x + dx] in PATH_FAMILY
            for dx in range(-2, 3) for dy in range(-2, 3)
        )
        if near_path:
            continue
        n = hash2(x // 4, y // 4, 11)
        if n > 0.72:
            continue  # lapangan kecil / pembukaan
        if n < 0.34 and x % 2 == 0 and y % 2 == 0:
            if hash2(x, y, 12) < 0.45:
                add_prop("oak_large", x, y)
            else:
                add_prop("pine_large", x, y)
        elif n >= 0.34 and hash2(x, y, 13) < 0.42:
            add_prop("pine_small" if hash2(x, y, 14) < 0.5 else "oak_small", x, y)

# --------------------------------------------------------------------------- #
# 7. DETAIL padang rumput: semak, batu, pagar, penunjuk, kayu
# --------------------------------------------------------------------------- #
for y in range(H):
    for x in range(W):
        if g[y][x] not in (GRASS, MEADOW):
            continue
        if (x, y) in prop_occ:
            continue
        in_ruins = RX - 2 <= x < RX + RW + 2 and RY - 2 <= y < RY + RH + 4
        if in_ruins:
            continue
        r = hash2(x, y, 21)
        if r < 0.020:
            add_prop(rng.choice(["bush_1", "bush_2", "bush_flowers", "bush_berry"]), x, y)
        elif r < 0.030:
            add_prop(rng.choice(["boulder_2", "rock_small"]), x, y)
        elif r < 0.034:
            add_prop("pebbles", x, y)
        elif r < 0.038:
            add_prop("stump" if hash2(x, y, 22) < 0.5 else "log", x, y)
        elif r < 0.041:
            add_prop("signpost", x, y, force=hash2(x, y, 23) < 0.35)

# --------------------------------------------------------------------------- #
# 8. ZONA, CHECKPOINT, SPAWN, MUSUH
# --------------------------------------------------------------------------- #
# urutan penting: zona paling spesifik di depan (dipakai "zona pertama yang cocok")
zones = [
    {"id": "ancient_ruins", "name": "Ancient Ruins", "rect": [RX - 3, RY - 5, RW + 8, RH + 11]},
    {"id": "greenwood_vale", "name": "Greenwood Vale", "rect": [VX - 14, VY - 6, 42, 36]},
    {"id": "old_road", "name": "Old Road", "rect": [VX + 26, VY + 2, 24, 42]},
    {"id": "north_woods", "name": "North Woods", "rect": [0, 0, W, 24]},
]

checkpoints = [
    {"id": "village_square", "name": "Alun-alun Desa", "col": plaza[0] + plaza[2] - 2,
     "row": plaza[1] + plaza[3] - 1, "zone": "greenwood_vale"},
    {"id": "bridge_camp", "name": "Api Unggun Jembatan", "col": bx1 + 3, "row": bridge_y - 1,
     "zone": "old_road"},
    {"id": "ruins_camp", "name": "Api Unggun Reruntuhan", "col": RX + 7, "row": RY + 6,
     "zone": "ancient_ruins"},
]

# Zona terbakar: MURNI DEKORATIF — tanah hangus lama (abu beterbangan + tunas
# tumbuh kembali lewat Particles.cs). Tidak mengubah walkable/terrain di atas.
burnt = [
    {"id": "vale_scorch", "name": "Bekas Bakaran Ladang", "rect": [33, 40, 8, 5]},
    {"id": "woods_scar", "name": "Luka Bakar Hutan Utara", "rect": [70, 6, 12, 8]},
    {"id": "ruins_scorch", "name": "Hangus Batas Reruntuhan", "rect": [RX + 16, RY + 14, 10, 7]},
]

enemies: list[dict] = []
for (etype, count, zone) in (("slime", 8, "greenwood_vale"), ("bat", 5, "north_woods"),
                             ("slime", 5, "old_road"), ("bat", 4, "old_road"),
                             ("warden", 1, "ancient_ruins"), ("bat", 4, "ancient_ruins")):
    placed = 0
    tries = 0
    while placed < count and tries < 4000:
        tries += 1
        x, y = rng.randint(0, W - 1), rng.randint(0, H - 1)
        z = None
        for zz in zones:
            zr = zz["rect"]
            if zr[0] <= x < zr[0] + zr[2] and zr[1] <= y < zr[1] + zr[3]:
                z = zz["id"]
                break
        if z != zone or g[y][x] not in WALK_TERRAIN or (x, y) in prop_occ:
            continue
        if any(abs(x - c["col"]) < 6 and abs(y - c["row"]) < 5 for c in checkpoints):
            continue
        enemies.append({"type": etype, "col": x, "row": y, "zone": zone})
        placed += 1


def zone_at(col: int, row: int) -> str | None:
    for z in zones:
        r = z["rect"]
        if r[0] <= col < r[0] + r[2] and r[1] <= row < r[1] + r[3]:
            return z["id"]
    return None


spawn = {"col": plaza[0] + 5, "row": plaza[1] + 6, "zone": zone_at(plaza[0] + 5, plaza[1] + 6)}

# --------------------------------------------------------------------------- #
# 9b. NPC DESA (sumber dialog + quest)
# --------------------------------------------------------------------------- #
NPC_SPECS = [
    {
        "id": "mira",
        "name": "Mira",
        "role": "pedagang pasar",
        "anchor": (plaza[0] + plaza[2] + 1, plaza[1] + 2),
        "facing": "down",
        "lines": [
            "Pasar Greenwood ramai pagi ini. Garam, kain, dan roti masih hangat.",
            "Tapi jalur utara jadi berbahaya. Slime turun dari hutan sejak semalam.",
        ],
        "quest": {
            "type": "kill",
            "target": "slime",
            "count": 5,
            "reward_xp": 120,
            "flag": "quest_slime_done",
            "intro": "Kalahkan 5 slime di padang utara, nanti kuberi upah.",
            "done_line": "Lima slime sudah jatuh. Ini upahmu, jangan habis di pasar.",
        },
    },
    {
        "id": "bram",
        "name": "Bram",
        "role": "penjaga gerbang",
        "anchor": (VX + 21, VY + 13),
        "facing": "side",
        "lines": [
            "Jembatan di timur satu-satunya jalan menyeberang sungai.",
            "Kalau mau ke reruntuhan, siapkan HP penuh. Ruin Warden tidak tidur.",
        ],
        "quest": None,
    },
    {
        "id": "sari",
        "name": "Sari",
        "role": "warga",
        "anchor": (plaza[0] - 3, plaza[1] + 1),
        "facing": "down",
        "lines": [
            "Dulu alun-alun ini tempat festival panen. Sekarang sumur itu yang tersisa.",
            "Istirahat di api unggun kalau HP-mu menipis — tekan E di dekatnya.",
        ],
        "quest": None,
    },
    {
        "id": "lena",
        "name": "Lena",
        "role": "petani ladang",
        "anchor": (VX + 23, VY + 10),
        "facing": "side",
        "lines": [
            "Ladangku di tepi utara ikut hangus waktu api lewat. Tanahnya masih hidup, syukurlah.",
            "Lihat tunas kecil di bekas terbakar? Dua minggu lagi padang itu hijau kembali.",
        ],
        "quest": None,
    },
    {
        "id": "tomas",
        "name": "Tomas",
        "role": "anak desa",
        "anchor": (plaza[0] + 2, plaza[1] - 2),
        "facing": "down",
        "lines": [
            "Aku lihat api besar di hutan utara! Bram bilang itu bukan api unggun biasa.",
            "Kalau aku besar nanti, aku mau jadi penjaga gerbang seperti Bram. Tapi aku takut warden.",
        ],
        "quest": None,
    },
]


def nearest_free(anchor: tuple[int, int], used: list[tuple[int, int]]) -> tuple[int, int]:
    """Cari tile walkable terdekat yang tidak tertutup prop & tidak dipakai NPC lain."""
    ax, ay = anchor
    for radius in range(0, 14):
        for dy in range(-radius, radius + 1):
            for dx in range(-radius, radius + 1):
                if max(abs(dx), abs(dy)) != radius:
                    continue
                x, y = ax + dx, ay + dy
                if not (0 <= x < W and 0 <= y < H):
                    continue
                if g[y][x] not in WALK_TERRAIN or (x, y) in prop_occ:
                    continue
                if any(abs(x - ux) < 3 and abs(y - uy) < 3 for ux, uy in used):
                    continue
                return x, y
    return ax, ay


npcs = []
used_spots: list[tuple[int, int]] = []
for spec in NPC_SPECS:
    col, row = nearest_free(spec["anchor"], used_spots)
    used_spots.append((col, row))
    npcs.append({
        "id": spec["id"],
        "name": spec["name"],
        "role": spec["role"],
        "col": col,
        "row": row,
        "facing": spec["facing"],
        "zone": zone_at(col, row),
        "lines": spec["lines"],
        "quest": spec["quest"],
    })


# --------------------------------------------------------------------------- #
# 9. walkability grid (tanah + prop solid) — dipakai engine untuk pathfinding
# --------------------------------------------------------------------------- #
# --------------------------------------------------------------------------- #
# 9c. PETI HARTA (interaktif: tombol E)
# --------------------------------------------------------------------------- #
CHEST_SPECS = [
    {"id": "chest_ruins", "anchor": (RX + 10, RY + 10), "gold": 80},
    {"id": "chest_cave", "anchor": (RX + 3, RY + 2), "gold": 60},
    {"id": "chest_forest", "anchor": (W // 2 + 7, 11), "gold": 40},
    {"id": "chest_market", "anchor": (plaza[0] + plaza[2] - 3, plaza[1] + plaza[3] + 2), "gold": 25},
    {"id": "chest_road", "anchor": (VX + 25, VY + 18), "gold": 55},
]

chests = []
for spec in CHEST_SPECS:
    col, row = nearest_free(spec["anchor"], used_spots)
    if not add_prop("chest_closed", col, row, force=True):
        continue
    used_spots.append((col, row))
    chests.append({"id": spec["id"], "col": col, "row": row, "gold": spec["gold"],
                   "zone": zone_at(col, row)})

solid_cell_groups: dict[str, set[tuple[int, int]]] = {}
for c in tileset["cells"]:
    if c["solid"]:
        solid_cell_groups.setdefault(c["group"], set()).add((c["dx"], c["dy"]))

walkable = [[1] * W for _ in range(H)]
for y in range(H):
    for x in range(W):
        if g[y][x] not in WALK_TERRAIN:
            walkable[y][x] = 0
for p in props:
    t = tileset["tiles"][p["group"]]
    for (dx, dy) in solid_cell_groups.get(p["group"], set()):
        cx, cy = p["col"] + dx, p["row"] + dy
        if in_bounds(cx, cy):
            walkable[cy][cx] = 0


# --------------------------------------------------------------------------- #
# 10. render tile ground
# --------------------------------------------------------------------------- #
def neighbor_mask(x: int, y: int) -> int:
    """bit 1=N, 2=E, 4=S, 8=W untuk keluarga permukaan yang sama."""
    fam = {DIRT, COBBLE, SAND}
    t = g[y][x]
    m = 0
    for (bit, dx, dy) in ((1, 0, -1), (2, 1, 0), (4, 0, 1), (8, -1, 0)):
        nx, ny = x + dx, y + dy
        if not in_bounds(nx, ny):
            m |= bit
            continue
        other = g[ny][nx]
        same = (t == WATER and other == WATER) or (t in fam and other in fam)
        if same:
            m |= bit
    return m


def ground_tile(x: int, y: int) -> str:
    t = g[y][x]
    h = hash2(x, y, 31)
    if t == GRASS:
        if h < 0.012:
            return ["grass_flowers_w", "grass_flowers_p", "grass_flowers_b", "grass_flowers_y"][int(h * 333) % 4]
        if h < 0.030:
            return "grass_tuft"
        if h < 0.040:
            return "grass_dirt_patch"
        if h < 0.048:
            return "grass_rocks"
        return f"grass_{1 + int(hash2(x, y, 32) * 4) % 4}"
    if t == MEADOW:
        if h < 0.30:
            return "meadow_tall"
        if h < 0.34:
            return "grass_flowers_mixed"
        return f"grass_{2 + int(hash2(x, y, 33) * 3) % 3}"
    if t == DIRT:
        return f"dirt_{neighbor_mask(x, y)}"
    if t == COBBLE:
        return f"cobble_{neighbor_mask(x, y)}"
    if t == SAND:
        return f"sand_{neighbor_mask(x, y)}"
    if t == WATER:
        if neighbor_mask(x, y) == 15:
            return f"water_deep_{int(hash2(x, y, 34) * 3) % 3}"
        return f"water_{neighbor_mask(x, y)}"
    if t == STONE:
        if h < 0.25:
            return "stone_floor_cracked"
        return f"stone_floor_{1 + int(h * 4) % 4}"
    if t == ROCK:
        rock_up = in_bounds(x, y - 1) and g[y - 1][x] == ROCK
        rock_left = in_bounds(x - 1, y) and g[y][x - 1] == ROCK
        rock_right = in_bounds(x + 1, y) and g[y][x + 1] == ROCK
        suffix = "l" if not rock_left else ("r" if not rock_right else "m")
        if not rock_up:
            return "cliff_top" if suffix == "m" else f"cliff_top_{suffix}"
        if not (in_bounds(x, y + 1) and g[y + 1][x] == ROCK):
            return "cliff_base"
        return "cliff_face" if suffix == "m" else f"cliff_face_{suffix}"
    if t == CAVE:
        return "cave_floor" if h > 0.12 else "cave_wall"
    if t == BRIDGE:
        left = in_bounds(x - 1, y) and g[y][x - 1] == BRIDGE
        right = in_bounds(x + 1, y) and g[y][x + 1] == BRIDGE
        return "bridge_deck_l" if not left else ("bridge_deck_r" if not right else "bridge_deck_m")
    raise ValueError(f"terrain tak dikenal di {x},{y}: {t}")


def ground_grid() -> list[list[str]]:
    """Nama tile ground per sel (hasil autotile) — dipakai engine supaya logika
    pemilihan tile hanya hidup di satu tempat (di sini)."""
    return [[ground_tile(x, y) for x in range(W)] for y in range(H)]


def render(scale: int = 1, with_markers: bool = True, with_labels: bool = True,
           with_props: bool = True) -> Image.Image:
    atlas = Image.open(TILES_DIR / "tileset.png").convert("RGBA")
    meta = tileset
    out = Image.new("RGBA", (W * TILE, H * TILE), (0, 0, 0, 255))

    def blit(name: str, col: int, row: int) -> None:
        t = meta["tiles"][name]
        crop = atlas.crop((t["x"], t["y"], t["x"] + t["w"] * TILE, t["y"] + t["h"] * TILE))
        out.alpha_composite(crop, (col * TILE, row * TILE))

    def blit_cell(cell: dict) -> None:
        r = cell["rect"]
        crop = atlas.crop((r[0], r[1], r[0] + r[2], r[1] + r[3]))
        out.alpha_composite(crop, (cell["col"] * TILE, cell["row"] * TILE))

    grid = ground_grid()
    for y in range(H):
        for x in range(W):
            blit(grid[y][x], x, y)

    cells_by_group: dict[str, list[dict]] = {}
    for c in meta["cells"]:
        cells_by_group.setdefault(c["group"], []).append(c)

    ordered = sorted(props, key=lambda p: (p["row"] + meta["tiles"][p["group"]]["base_row"], p["col"])) if with_props else []
    for p in ordered:
        for c in cells_by_group[p["group"]]:
            blit_cell({"rect": c["rect"],
                       "col": c["dx"] + p["col"], "row": c["dy"] + p["row"]})

    if with_markers:
        d = ImageDraw.Draw(out)
        sk = spawn
        d.rectangle([sk["col"] * TILE + 3, sk["row"] * TILE + 3, sk["col"] * TILE + 12, sk["row"] * TILE + 12],
                    outline=(0x6B, 0x93, 0xC9, 255), width=2)
        for c in checkpoints:
            d.ellipse([c["col"] * TILE + 2, c["row"] * TILE + 2, c["col"] * TILE + 13, c["row"] * TILE + 13],
                      outline=(0xE8, 0xC8, 0x5A, 255), width=2)
        for e in enemies:
            col = {"slime": (0x6F, 0xA3, 0x48), "bat": (0x7F, 0x9F, 0xD4), "warden": (0xD4, 0x78, 0x8F)}[e["type"]]
            d.ellipse([e["col"] * TILE + 5, e["row"] * TILE + 5, e["col"] * TILE + 10, e["row"] * TILE + 10],
                      fill=col)
        for z in zones:
            r = z["rect"]
            d.rectangle([r[0] * TILE, r[1] * TILE, (r[0] + r[2]) * TILE, (r[1] + r[3]) * TILE],
                        outline=(0xFF, 0x00, 0xFF, 100), width=1)
            if with_labels:
                d.text((r[0] * TILE + 4, r[1] * TILE + 3), z["name"], fill=(0xFF, 0xFF, 0xFF, 255))
        for b in burnt:
            r = b["rect"]
            d.rectangle([r[0] * TILE, r[1] * TILE, (r[0] + r[2]) * TILE, (r[1] + r[3]) * TILE],
                        outline=(0xC0, 0x50, 0x30, 220), width=1)
            if with_labels:
                d.text((r[0] * TILE + 4, (r[1] + r[3]) * TILE - 12), b["name"], fill=(0xE0, 0x88, 0x60, 255))
        for c in checkpoints:
            if with_labels:
                d.text((c["col"] * TILE - 24, c["row"] * TILE + 16), c["name"], fill=(0xE8, 0xC8, 0x5A, 255))

    if scale != 1:
        out = out.resize((out.width * scale, out.height * scale), Image.NEAREST)
    return out


def main() -> None:
    WORLD_DIR.mkdir(parents=True, exist_ok=True)

    grid = ground_grid()
    tile_names = sorted({n for row in grid for n in row})
    name_idx = {n: i for i, n in enumerate(tile_names)}
    data = {
        "tile_size": TILE,
        "width": W,
        "height": H,
        "terrain_names": TERRAIN_NAMES,
        "terrain": g,
        "tile_names": tile_names,
        "ground_tiles": [[name_idx[n] for n in row] for row in grid],
        "props": props,
        "walkable": walkable,
        "markers": {
            "spawn": spawn,
            "zones": zones,
            "checkpoints": checkpoints,
            "burnt": burnt,
            "enemies": enemies,
            "npcs": npcs,
            "chests": chests,
        },
        "source": "tools/generate_world_map.py",
    }
    (WORLD_DIR / "overworld.json").write_text(json.dumps(data) + "\n")
    # pratinjau untuk manusia -> docs/ (tidak ikut di-import engine)
    docs = Path("docs")
    docs.mkdir(parents=True, exist_ok=True)
    render(scale=1, with_markers=True).convert("RGB").save(docs / "preview-world.png")
    render(scale=1, with_markers=False).convert("RGB").save(docs / "preview-world-clean.png")
    # layer tanah tanpa prop -> cadangan kalau TileMapLayer tidak dipakai
    render(with_markers=False, with_props=False).convert("RGB").save(WORLD_DIR / "overworld_ground.png")

    walk_tiles = sum(sum(r) for r in walkable)
    print(f"overworld.json: {W}x{H} tile ({W * TILE}x{H * TILE} px), {len(props)} prop, "
          f"{len(enemies)} musuh, {len(npcs)} NPC, {len(chests)} peti, {len(zones)} zona, "
          f"{len(burnt)} zona terbakar")
    print(f"walkable: {walk_tiles}/{W * H} tile ({walk_tiles / (W * H):.1%})")
    print(f"preview: docs/preview-world.png")


if __name__ == "__main__":
    main()
