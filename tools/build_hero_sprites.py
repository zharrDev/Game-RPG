#!/usr/bin/env python3
"""Rakit sprite hero untuk game Godot: atlas + JSON.

Sumber frame:
  1. GERAK 4 ARAH  -> prosedural (tools/generate_topdown_frames.py), supaya
     idle/walk/run untuk atas/bawah/samping konsisten dan bebas jitter.
  2. POSE AKSI     -> frame AI di assets/character/frames/character/*.png
     (attack, skill, hurt, death, jump_3), dibersihkan: alpha di-binerkan,
     warna di-snap ke palet Arga, bintik tunggal dibuang, lalu diskalakan dan
     disejajarkan (kaki di garis tanah) supaya seukuran dengan sprite prosedural.
  3. FRAME KURANG  -> jump_1, jump_2, death_2 digambar prosedural memakai
     generator side-view (tools/generate_character_frames.py).

Output:
  game/assets/character/hero_atlas.png   atlas siap dipakai (sel 64x40)
  game/assets/character/hero.json        indeks frame + definisi animasi
  docs/preview-hero.png                  pratinjau semua frame (untuk ditinjau)

Jalankan:
    python3 tools/build_hero_sprites.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))

import generate_character_frames as gcf  # noqa: E402  (generator side-view prosedural)
import generate_topdown_frames as td  # noqa: E402  (generator top-down 4 arah)

CELL_W, CELL_H = td.CELL_W, td.CELL_H
FOOT_X, FOOT_Y = td.CX, td.FOOT_Y
TARGET_H = 24                      # tinggi bbox hero (px) — sama untuk semua frame
AI_DIR = ROOT / "assets/character/frames/character"
OUT_DIR = ROOT / "game/assets/character"
DOCS = ROOT / "docs"

# aksi dari frame AI; yang tidak ada di folder akan digambar prosedural
AI_ACTIONS = ["attack_1", "attack_2", "attack_3", "skill_1", "skill_2", "skill_3",
              "hurt_1", "death_1", "death_3", "jump_3"]
PROC_FILL = ["jump_1", "jump_2", "death_2"]

PALETTE: list[tuple[int, int, int]] = []
for c in (gcf.SKIN, gcf.SKIN_S, gcf.SKIN_H, gcf.HAIR, gcf.HAIR_S, td.HAIR_H,
          gcf.TUNIC, gcf.TUNIC_S, gcf.TUNIC_H, gcf.ARMOR, gcf.ARMOR_S, gcf.ARMOR_H,
          gcf.LEATHER, gcf.LEATHER_S, td.LEATHER_H, gcf.BLADE, gcf.BLADE_S, gcf.BLADE_H,
          gcf.OUTLINE, gcf.GLOW_A, gcf.GLOW_B, gcf.WISP):
    PALETTE.append(tuple(c[:3]))

_snap_cache: dict[tuple[int, int, int], tuple[int, int, int]] = {}


def snap(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    if rgb not in _snap_cache:
        best, best_d = PALETTE[0], 1 << 30
        for p in PALETTE:
            d = (p[0] - rgb[0]) ** 2 + (p[1] - rgb[1]) ** 2 + (p[2] - rgb[2]) ** 2
            if d < best_d:
                best, best_d = p, d
        _snap_cache[rgb] = best
    return _snap_cache[rgb]


def despeckle(px: list[tuple[int, int, int, int]], w: int, h: int) -> int:
    """Buang piksel yatim (kurang dari 2 tetangga 8-arah)."""
    removed = 0
    for y in range(h):
        for x in range(w):
            i = y * w + x
            if px[i][3] == 0:
                continue
            n = 0
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dx == 0 and dy == 0:
                        continue
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and px[ny * w + nx][3] > 0:
                        n += 1
            if n < 2:
                px[i] = (0, 0, 0, 0)
                removed += 1
    return removed


def prepare(img: Image.Image, scale: float) -> tuple[Image.Image, int]:
    """Bersihkan frame (alpha biner, snap palet, buang bintik) + skala."""
    im = img.convert("RGBA")
    w, h = im.size
    px = list(im.getdata())
    for i, (r, g, b, a) in enumerate(px):
        if a < 110:
            px[i] = (0, 0, 0, 0)
        else:
            sr, sg, sb = snap((r, g, b))
            px[i] = (sr, sg, sb, 255)
    clean = Image.new("RGBA", (w, h))
    clean.putdata(px)
    removed = despeckle(px, w, h)
    clean.putdata(px)

    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    small = clean.resize((nw, nh), Image.BOX)
    # setelah down-scale, ulangi binerisasi (menghapus tepi setengah transparan)
    sp = list(small.getdata())
    for i, (r, g, b, a) in enumerate(sp):
        if a < 110:
            sp[i] = (0, 0, 0, 0)
        else:
            sr, sg, sb = snap((r, g, b))
            sp[i] = (sr, sg, sb, 255)
    small.putdata(sp)
    return small, removed


def content_bbox(im: Image.Image):
    return im.getchannel("A").getbbox()


def feet_center_x(im: Image.Image, box) -> float:
    """Pusat-x dari massa piksel di 25% baris terbawah — dipakai untuk menyelaraskan kaki."""
    x0, y0, x1, y1 = box
    y_cut = y1 - max(1, (y1 - y0) // 4)
    total, acc = 0, 0
    px = im.load()
    for y in range(y_cut, y1):
        for x in range(x0, x1):
            if px[x, y][3] > 0:
                acc += x
                total += 1
    return acc / total if total else (x0 + x1) / 2


def place_in_cell(im: Image.Image) -> Image.Image:
    """Taruh frame di sel 64x40: kaki di garis tanah, badan terpusat di FOOT_X."""
    cell = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
    box = content_bbox(im)
    if box is None:
        return cell
    cx = feet_center_x(im, box)
    dx = int(round(FOOT_X - cx))
    dy = int(round(FOOT_Y + 1 - box[3]))
    cell.alpha_composite(im, (dx, dy))
    return cell


def load_ai_frames(scale: float) -> tuple[dict[str, Image.Image], list[str], dict[str, int]]:
    frames: dict[str, Image.Image] = {}
    missing: list[str] = []
    speckles: dict[str, int] = {}
    for name in AI_ACTIONS:
        path = AI_DIR / f"{name}.png"
        if not path.exists():
            missing.append(name)
            continue
        with Image.open(path) as raw:
            prepared, removed = prepare(raw, scale)
        speckles[name] = removed
        frames[name] = place_in_cell(prepared)
    return frames, missing, speckles


def load_proc_frames(scale: float) -> dict[str, Image.Image]:
    frames: dict[str, Image.Image] = {}
    for name in PROC_FILL:
        if name in gcf.PRONE_FRAMES:
            art, glow = gcf.render_prone(gcf.PRONE_FRAMES[name])
        else:
            art, glow = gcf.render_pose(gcf.POSES[name])
        img = gcf.with_outline(art, glow).to_image()
        prepared, _ = prepare(img, scale)
        frames[name] = place_in_cell(prepared)
    return frames


def build() -> dict:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    DOCS.mkdir(parents=True, exist_ok=True)

    # ---- 1. gerak 4 arah (prosedural) — sudah sejajar dari generatornya, jangan digeser lagi
    movement = td.all_frames()          # {"down_idle": [img, ...], ...}

    # skala untuk frame AI & frame prosedural side-view, diukur dari idle_1 masing-masing
    ai_ref = Image.open(AI_DIR / "idle_1.png").convert("RGBA")
    ai_scale = TARGET_H / (content_bbox(ai_ref)[3] - content_bbox(ai_ref)[1])
    art, glow = gcf.render_pose(gcf.POSES["idle_1"])
    proc_idle = gcf.with_outline(art, glow).to_image()
    proc_scale = TARGET_H / (content_bbox(proc_idle)[3] - content_bbox(proc_idle)[1])

    # ---- 2. pose aksi dari frame AI + ---- 3. frame yang tidak ada (prosedural)
    ai_frames, missing, speckles = load_ai_frames(ai_scale)
    proc_frames = load_proc_frames(proc_scale)
    actions = {**proc_frames, **ai_frames}

    # ---- 4. susun atlas
    order: list[tuple[str, Image.Image, str]] = []
    anim_defs: dict[str, dict] = {}
    for direction in td.DIRECTIONS:
        side_dir = "side" if direction in ("left", "right") else direction
        for anim, count in td.ANIMATIONS.items():
            key = f"{anim}_{side_dir}"
            if key not in anim_defs:
                anim_defs[key] = {"frames": [], "fps": {"idle": 6, "walk": 10, "run": 12}[anim],
                                  "loop": True, "mirror": side_dir == "side", "kind": "movement"}
            for i in range(count):
                name = f"{direction}_{anim}_{i + 1}"
                order.append((name, movement[f"{direction}_{anim}"][i], "procedural"))
                if direction == "right" or direction in ("down", "up"):
                    anim_defs[key]["frames"].append(name)

    action_order = ["attack_1", "attack_2", "attack_3", "skill_1", "skill_2", "skill_3",
                    "hurt_1", "death_1", "death_2", "death_3", "jump_1", "jump_2", "jump_3"]
    anim_defs["attack"] = {"frames": ["attack_1", "attack_2", "attack_3"], "fps": 12, "loop": False,
                           "mirror": True, "kind": "action"}
    anim_defs["skill"] = {"frames": ["skill_1", "skill_2", "skill_3"], "fps": 10, "loop": False,
                          "mirror": True, "kind": "action"}
    anim_defs["hurt"] = {"frames": ["hurt_1"], "fps": 12, "loop": False, "mirror": True, "kind": "action"}
    anim_defs["death"] = {"frames": ["death_1", "death_2", "death_3"], "fps": 6, "loop": False,
                          "mirror": True, "kind": "action"}
    anim_defs["jump"] = {"frames": ["jump_1", "jump_2", "jump_3"], "fps": 10, "loop": False,
                         "mirror": True, "kind": "action"}
    for name in action_order:
        src = "ai" if name in ai_frames else "procedural"
        order.append((name, actions[name], src))

    columns = 8
    rows = (len(order) + columns - 1) // columns
    atlas = Image.new("RGBA", (columns * CELL_W, rows * CELL_H), (0, 0, 0, 0))
    frame_index: dict[str, dict] = {}
    for i, (name, im, src) in enumerate(order):
        col, row = i % columns, i // columns
        atlas.paste(im, (col * CELL_W, row * CELL_H))
        facing = "side" if src == "ai" or name in action_order else name.split("_")[0]
        frame_index[name] = {"col": col, "row": row, "x": col * CELL_W, "y": row * CELL_H,
                             "w": CELL_W, "h": CELL_H, "source": src, "facing": facing}
    atlas.save(OUT_DIR / "hero_atlas.png")

    meta = {
        "cell": {"w": CELL_W, "h": CELL_H},
        "foot": {"x": FOOT_X, "y": FOOT_Y},
        "atlas": {"file": "hero_atlas.png", "columns": columns, "rows": rows,
                  "width": atlas.width, "height": atlas.height},
        "frame_count": len(order),
        "frames": frame_index,
        "animations": anim_defs,
        "side_animation_faces_right": True,
        "sources": {
            "movement": "prosedural (tools/generate_topdown_frames.py)",
            "action_ai": sorted(ai_frames),
            "action_procedural": sorted(proc_frames),
            "missing_from_ai": missing,
            "ai_scale": round(ai_scale, 4),
            "proc_scale": round(proc_scale, 4),
        },
    }
    (OUT_DIR / "hero.json").write_text(json.dumps(meta, indent=2) + "\n")

    # ---- 5. pratinjau
    grass = Image.open(ROOT / "game/assets/tiles/tileset.png").convert("RGBA")
    tiles = json.loads((ROOT / "game/assets/tiles/tileset.json").read_text())
    g = tiles["tiles"]["grass_1"]
    tile = grass.crop((g["x"], g["y"], g["x"] + 16, g["y"] + 16))
    scale_preview = 3
    pw, ph = atlas.width * scale_preview, atlas.height * scale_preview + 18
    preview = Image.new("RGBA", (pw, ph), (0x22, 0x22, 0x2A, 255))
    bg = Image.new("RGBA", (atlas.width, atlas.height))
    for ty in range(0, atlas.height, 16):
        for tx in range(0, atlas.width, 16):
            bg.alpha_composite(tile, (tx, ty))
    preview.alpha_composite(bg.resize((atlas.width * scale_preview, atlas.height * scale_preview), Image.NEAREST), (0, 18))
    preview.alpha_composite(atlas.resize((pw, atlas.height * scale_preview), Image.NEAREST), (0, 18))
    d = ImageDraw.Draw(preview)
    d.text((4, 4), f"hero atlas - {len(order)} frames, cell {CELL_W}x{CELL_H}, ground line y={FOOT_Y}",
           fill=(255, 255, 255))
    for i, (name, _im, src) in enumerate(order):
        col, row = i % columns, i // columns
        d.rectangle([col * CELL_W * scale_preview, 18 + row * CELL_H * scale_preview,
                     (col + 1) * CELL_W * scale_preview - 1, 18 + (row + 1) * CELL_H * scale_preview - 1],
                    outline=(0xFF, 0x00, 0xFF, 90))
        d.text((col * CELL_W * scale_preview + 2, row * CELL_H * scale_preview + 20),
               name + ("*" if src == "ai" else ""), fill=(0xE8, 0xC8, 0x5A, 255))
    preview.convert("RGB").save(DOCS / "preview-hero.png")

    print(f"hero_atlas.png : {atlas.width}x{atlas.height} ({len(order)} frame, {columns}x{rows} sel)")
    print(f"  gerak prosedural : {sum(1 for _,_,s in order if s == 'procedural')} frame")
    print(f"  aksi dari AI     : {len(ai_frames)} frame ({', '.join(sorted(ai_frames)) if ai_frames else '-'})")
    print(f"  aksi prosedural  : {', '.join(sorted(proc_frames))}")
    if missing:
        print(f"  CATATAN: frame AI tidak ada -> {', '.join(missing)}")
    if speckles:
        print(f"  bintik dibuang   : {speckles}")
    print(f"preview: {DOCS / 'preview-hero.png'}")
    return meta


if __name__ == "__main__":
    build()
