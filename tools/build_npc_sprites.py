#!/usr/bin/env python3
"""Rakit atlas sprite NPC desa (3 warga) memakai generator top-down yang sama
dengan hero, hanya palet baju/rambut yang diganti — jadi outline, shading,
proporsi, dan ukurannya identik dengan Arga dan musuh.

Output:
  game/assets/character/npc_atlas.png   12 frame (3 NPC x idle down/side)
  game/assets/character/npc.json        indeks frame + animasi idle
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

# Palet per NPC. Kunci = konstanta di generate_topdown_frames yang ditimpa.
PALETTES: dict[str, dict[str, tuple[int, int, int, int]]] = {
    # pedagang pasar: tunik hijau lumut, celemek kulit, rambut gelap
    "mira": {
        "TUNIC": (0x4F, 0x7A, 0x4A, 255), "TUNIC_S": (0x36, 0x57, 0x33, 255), "TUNIC_H": (0x72, 0x9E, 0x68, 255),
        "HAIR": (0x3A, 0x2A, 0x22, 255), "HAIR_S": (0x27, 0x1B, 0x16, 255), "HAIR_H": (0x55, 0x3E, 0x31, 255),
        "ARMOR": (0xC9, 0xB3, 0x86, 255), "ARMOR_S": (0x93, 0x7E, 0x58, 255), "ARMOR_H": (0xEA, 0xD6, 0xA8, 255),
        "LEATHER": (0x7A, 0x4F, 0x2C, 255), "LEATHER_S": (0x54, 0x35, 0x1C, 255), "LEATHER_H": (0x99, 0x6B, 0x3D, 255),
    },
    # penjaga gerbang: zirah baja, tunik merah bata
    "bram": {
        "TUNIC": (0x8C, 0x3D, 0x33, 255), "TUNIC_S": (0x63, 0x29, 0x22, 255), "TUNIC_H": (0xB0, 0x53, 0x46, 255),
        "HAIR": (0x5A, 0x3A, 0x20, 255), "HAIR_S": (0x3D, 0x26, 0x14, 255), "HAIR_H": (0x7A, 0x51, 0x2E, 255),
        "ARMOR": (0x9A, 0xA6, 0xB2, 255), "ARMOR_S": (0x6B, 0x76, 0x82, 255), "ARMOR_H": (0xCF, 0xD8, 0xE2, 255),
    },
    # warga tua: tunik krem, rambut putih, tanpa zirah
    "sari": {
        "TUNIC": (0xC7, 0xBE, 0xA2, 255), "TUNIC_S": (0x97, 0x8F, 0x76, 255), "TUNIC_H": (0xE4, 0xDC, 0xC2, 255),
        "HAIR": (0xD8, 0xD4, 0xC8, 255), "HAIR_S": (0xA8, 0xA4, 0x98, 255), "HAIR_H": (0xF2, 0xEF, 0xE6, 255),
        "ARMOR": (0xC7, 0xBE, 0xA2, 255), "ARMOR_S": (0x97, 0x8F, 0x76, 255), "ARMOR_H": (0xE4, 0xDC, 0xC2, 255),
    },
}

KEYS = ("TUNIC", "TUNIC_S", "TUNIC_H", "HAIR", "HAIR_S", "HAIR_H",
        "ARMOR", "ARMOR_S", "ARMOR_H", "LEATHER", "LEATHER_S", "LEATHER_H")

IDLE_FRAMES = 2


def render_npc(palette: dict[str, tuple[int, int, int, int]]) -> dict[str, list[Image.Image]]:
    """Timpa palet modul generator, render, lalu kembalikan palet aslinya."""
    saved = {key: getattr(td, key) for key in KEYS}
    try:
        for key, value in palette.items():
            setattr(td, key, value)
        frames = td.all_frames()
    finally:
        for key, value in saved.items():
            setattr(td, key, value)
    return {
        "idle_down": frames["down_idle"][:IDLE_FRAMES],
        "idle_side": frames["right_idle"][:IDLE_FRAMES],
    }


def build() -> dict:
    OUT.mkdir(parents=True, exist_ok=True)
    DOCS.mkdir(parents=True, exist_ok=True)

    frames: list[tuple[str, str, Image.Image]] = []
    animations: dict[str, dict] = {}
    for npc_id, palette in PALETTES.items():
        rendered = render_npc(palette)
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
    for npc_id in PALETTES:
        per_npc[npc_id] = {
            "idle_down": animations[f"{npc_id}:idle_down"],
            "idle_side": animations[f"{npc_id}:idle_side"],
        }

    meta = {
        "cell": {"w": CELL_W, "h": CELL_H},
        "foot": {"x": FOOT[0], "y": FOOT[1]},
        "atlas": {"file": "npc_atlas.png", "columns": columns, "rows": rows,
                  "width": atlas.width, "height": atlas.height},
        "frame_count": len(frames),
        "frames": index,
        "animations": per_npc,
        "palettes": {npc_id: {k: "#%02X%02X%02X" % v[:3] for k, v in palette.items()}
                     for npc_id, palette in PALETTES.items()},
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
    print(f"  NPC: {', '.join(PALETTES)}")
    print(f"preview: {DOCS / 'preview-npcs.png'}")
    return meta


if __name__ == "__main__":
    build()
