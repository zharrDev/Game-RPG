#!/usr/bin/env python3
"""Rakit atlas sprite NPC desa (5 warga) memakai generator top-down yang sama
dengan hero — outline, shading, proporsi, dan ukurannya identik dengan Arga.

Tiap warga punya identitas sendiri:
  palet baju/rambut (12 kunci warna) + fitur (tanpa pedang/zirah bahu, gaya
  rambut, topi jerami, celemek, tongkat) dan Tomas memakai metrik badan anak.

Output:
  game/assets/character/npc_atlas.png   60 frame (5 NPC x idle+walk, 2 arah)
  game/assets/character/npc.json        indeks frame + animasi idle/walk
  docs/preview-npcs.png                 pratinjau

Jalankan:
    python3 tools/build_npc_sprites.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))

import generate_topdown_frames as td  # noqa: E402

OUT = ROOT / "game" / "assets" / "character"
DOCS = ROOT / "docs"
CELL_W, CELL_H = td.CELL_W, td.CELL_H
FOOT = (td.CX, td.FOOT_Y)

STRAW = (0xC9, 0xA4, 0x55, 255)          # topi jerami (Lena)
APRON_L = (0x8A, 0x6B, 0x43, 255)        # celemek kulit (Lena)
APRON_M = (0x6B, 0x4A, 0x28, 255)        # celemek gelap (Mira)
STAFF_W = (0x7A, 0x4F, 0x2A, 255)        # tongkat kayu (Sari)

# per-warga: palet (12 kunci warna), fitur opsional, dan metrik khusus anak.
NPCS: dict[str, dict] = {
    # pedagang pasar: tunik hijau lumut, celemek kulit, rambut gelap disanggul
    "mira": {
        "palette": {
            "TUNIC": (0x4F, 0x7A, 0x4A, 255), "TUNIC_S": (0x36, 0x57, 0x33, 255), "TUNIC_H": (0x72, 0x9E, 0x68, 255),
            "HAIR": (0x3A, 0x2A, 0x22, 255), "HAIR_S": (0x27, 0x1B, 0x16, 255), "HAIR_H": (0x55, 0x3E, 0x31, 255),
            "ARMOR": (0xC9, 0xB3, 0x86, 255), "ARMOR_S": (0x93, 0x7E, 0x58, 255), "ARMOR_H": (0xEA, 0xD6, 0xA8, 255),
            "LEATHER": (0x7A, 0x4F, 0x2C, 255), "LEATHER_S": (0x54, 0x35, 0x1C, 255), "LEATHER_H": (0x99, 0x6B, 0x3D, 255),
        },
        "features": {"OPT_SWORD": False, "OPT_PAULDRON": False, "OPT_HAIR": "bun", "OPT_APRON": APRON_M},
    },
    # penjaga gerbang: zirah baja + pedang, tunik merah bata
    "bram": {
        "palette": {
            "TUNIC": (0x8C, 0x3D, 0x33, 255), "TUNIC_S": (0x63, 0x29, 0x22, 255), "TUNIC_H": (0xB0, 0x53, 0x46, 255),
            "HAIR": (0x5A, 0x3A, 0x20, 255), "HAIR_S": (0x3D, 0x26, 0x14, 255), "HAIR_H": (0x7A, 0x51, 0x2E, 255),
            "ARMOR": (0x9A, 0xA6, 0xB2, 255), "ARMOR_S": (0x6B, 0x76, 0x82, 255), "ARMOR_H": (0xCF, 0xD8, 0xE2, 255),
        },
        "features": {"OPT_HAIR": "short"},
    },
    # warga tua: tunik krem, rambut putih panjang, bertongkat
    "sari": {
        "palette": {
            "TUNIC": (0xC7, 0xBE, 0xA2, 255), "TUNIC_S": (0x97, 0x8F, 0x76, 255), "TUNIC_H": (0xE4, 0xDC, 0xC2, 255),
            "HAIR": (0xD8, 0xD4, 0xC8, 255), "HAIR_S": (0xA8, 0xA4, 0x98, 255), "HAIR_H": (0xF2, 0xEF, 0xE6, 255),
            "ARMOR": (0xC7, 0xBE, 0xA2, 255), "ARMOR_S": (0x97, 0x8F, 0x76, 255), "ARMOR_H": (0xE4, 0xDC, 0xC2, 255),
        },
        "features": {"OPT_SWORD": False, "OPT_PAULDRON": False, "OPT_HAIR": "long", "OPT_STAFF": STAFF_W},
    },
    # petani ladang: topi jerami + celemek, tunik zaitun (warga baru)
    "lena": {
        "palette": {
            "TUNIC": (0x6E, 0x7A, 0x3A, 255), "TUNIC_S": (0x4C, 0x56, 0x28, 255), "TUNIC_H": (0x8F, 0x9C, 0x52, 255),
            "HAIR": (0x4A, 0x32, 0x1E, 255), "HAIR_S": (0x33, 0x22, 0x13, 255), "HAIR_H": (0x68, 0x48, 0x2C, 255),
            "ARMOR": (0x6E, 0x7A, 0x3A, 255), "ARMOR_S": (0x4C, 0x56, 0x28, 255), "ARMOR_H": (0x8F, 0x9C, 0x52, 255),
        },
        "features": {"OPT_SWORD": False, "OPT_PAULDRON": False, "OPT_HAIR": "bun", "OPT_HAT": STRAW, "OPT_APRON": APRON_L},
    },
    # anak desa: badan lebih pendek & ramping, tunik jingga cerah (warga baru)
    "tomas": {
        "palette": {
            "TUNIC": (0xC9, 0x7A, 0x42, 255), "TUNIC_S": (0x94, 0x55, 0x2B, 255), "TUNIC_H": (0xE8, 0x9C, 0x5E, 255),
            "HAIR": (0x2A, 0x20, 0x1C, 255), "HAIR_S": (0x1A, 0x13, 0x10, 255), "HAIR_H": (0x44, 0x34, 0x2C, 255),
            "ARMOR": (0xC9, 0x7A, 0x42, 255), "ARMOR_S": (0x94, 0x55, 0x2B, 255), "ARMOR_H": (0xE8, 0x9C, 0x5E, 255),
            "LEATHER": (0x6B, 0x4A, 0x28, 255), "LEATHER_S": (0x4E, 0x35, 0x1C, 255), "LEATHER_H": (0x8A, 0x61, 0x36, 255),
        },
        "features": {"OPT_SWORD": False, "OPT_PAULDRON": False, "OPT_HAIR": "short"},
        "metrics": {
            "HEAD_TOP": td.FOOT_Y - 19, "HEAD_H": 6, "BODY_W": 9,
            "BODY_X": td.CX - 9 // 2, "TORSO_TOP": td.FOOT_Y - 19 + 6, "LEG_H": 2,
        },
    },
}

PALETTE_KEYS = ("TUNIC", "TUNIC_S", "TUNIC_H", "HAIR", "HAIR_S", "HAIR_H",
                "ARMOR", "ARMOR_S", "ARMOR_H", "LEATHER", "LEATHER_S", "LEATHER_H")
FEATURE_KEYS = ("OPT_SWORD", "OPT_PAULDRON", "OPT_HAIR", "OPT_HAT", "OPT_APRON", "OPT_STAFF")
METRIC_KEYS = ("HEAD_TOP", "HEAD_H", "BODY_W", "BODY_X", "TORSO_TOP", "LEG_H")

ANIMATIONS = {
    "idle_down": ("down_idle", 2),
    "idle_side": ("right_idle", 2),
    "walk_down": ("down_walk", 4),
    "walk_side": ("right_walk", 4),
}


def render_npc(spec: dict) -> dict[str, list[Image.Image]]:
    """Timpa palet/fitur/metrik modul generator, render, lalu kembalikan semuanya."""
    overrides: dict = dict(spec["palette"])
    overrides.update(spec.get("features", {}))
    overrides.update(spec.get("metrics", {}))
    saved = {key: getattr(td, key) for key in overrides}
    try:
        for key, value in overrides.items():
            setattr(td, key, value)
        frames = td.all_frames()
    finally:
        for key, value in saved.items():
            setattr(td, key, value)
    return {anim: frames[source][:count] for anim, (source, count) in ANIMATIONS.items()}


def build() -> dict:
    OUT.mkdir(parents=True, exist_ok=True)
    DOCS.mkdir(parents=True, exist_ok=True)

    frames: list[tuple[str, str, Image.Image]] = []
    animations: dict[str, dict] = {}
    for npc_id, spec in NPCS.items():
        rendered = render_npc(spec)
        for anim, images in rendered.items():
            animations[f"{npc_id}:{anim}"] = [f"{npc_id}:{anim}_{i + 1}" for i in range(len(images))]
            for index, image in enumerate(images):
                frames.append((f"{npc_id}:{anim}_{index + 1}", anim, image))

    columns = 6
    rows = (len(frames) + columns - 1) // columns
    atlas = Image.new("RGBA", (columns * CELL_W, rows * CELL_H), (0, 0, 0, 0))
    index: dict[str, dict] = {}
    for position, (name, _anim, image) in enumerate(frames):
        col, row = position % columns, position // columns
        atlas.paste(image, (col * CELL_W, row * CELL_H))
        index[name] = {
            "col": col, "row": row,
            "x": col * CELL_W, "y": row * CELL_H,
            "w": CELL_W, "h": CELL_H,
        }
    atlas.save(OUT / "npc_atlas.png")

    per_npc: dict[str, dict] = {}
    for npc_id in NPCS:
        per_npc[npc_id] = {anim: animations[f"{npc_id}:{anim}"] for anim in ANIMATIONS}

    meta = {
        "cell": {"w": CELL_W, "h": CELL_H},
        "foot": {"x": FOOT[0], "y": FOOT[1]},
        "atlas": {"file": "npc_atlas.png", "columns": columns, "rows": rows,
                  "width": atlas.width, "height": atlas.height},
        "frame_count": len(frames),
        "frames": index,
        "animations": per_npc,
        "palettes": {npc_id: {k: "#%02X%02X%02X" % v[:3] for k, v in spec["palette"].items()}
                     for npc_id, spec in NPCS.items()},
        "features": {npc_id: {k: (list(v) if isinstance(v, tuple) else v)
                              for k, v in spec.get("features", {}).items()}
                     for npc_id, spec in NPCS.items()},
        "child": "tomas",
        "source": "tools/build_npc_sprites.py",
    }
    (OUT / "npc.json").write_text(json.dumps(meta, indent=2) + "\n")

    # pratinjau di atas rumput
    tileset = Image.open(ROOT / "game/assets/tiles/tileset.png").convert("RGBA")
    tiles = json.loads((ROOT / "game/assets/tiles/tileset.json").read_text())["tiles"]
    grass = tiles["grass_1"]
    tile = tileset.crop((grass["x"], grass["y"], grass["x"] + 16, grass["y"] + 16))
    zoom = 3
    preview = Image.new("RGBA", (atlas.width * zoom, atlas.height * zoom + 16), (0x22, 0x22, 0x2A, 255))
    background = Image.new("RGBA", atlas.size)
    for y in range(0, atlas.height, 16):
        for x in range(0, atlas.width, 16):
            background.alpha_composite(tile, (x, y))
    preview.alpha_composite(background.resize((atlas.width * zoom, atlas.height * zoom), Image.NEAREST), (0, 16))
    preview.alpha_composite(atlas.resize((atlas.width * zoom, atlas.height * zoom), Image.NEAREST), (0, 16))
    draw = ImageDraw.Draw(preview)
    draw.text((4, 3), f"NPC atlas - {len(frames)} frames, cell {CELL_W}x{CELL_H}", fill=(255, 255, 255))
    preview.convert("RGB").save(DOCS / "preview-npcs.png")

    print(f"npc_atlas.png: {atlas.width}x{atlas.height} ({len(frames)} frame, {columns}x{rows} sel)")
    print(f"  NPC: {', '.join(NPCS)} (anak: {meta['child']})")
    print(f"preview: {DOCS / 'preview-npcs.png'}")
    return meta


if __name__ == "__main__":
    build()
