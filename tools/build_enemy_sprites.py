#!/usr/bin/env python3
"""Rakit sprite musuh: slime & kelelawar prosedural + boss dari frame AI.

  · slime, bat        -> digambar prosedural (palet & gaya sama dengan hero)
  · warden (boss)     -> frame AI di assets/character/frames/enemies/*.png,
                         dibersihkan (alpha biner, snap palet, buang bintik)
                         lalu diskalakan + disejajarkan kaki.

Output:
  game/assets/character/enemy_atlas.png
  game/assets/character/enemy.json
  docs/preview-enemies.png

Jalankan:
    python3 tools/build_enemy_sprites.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))

from build_hero_sprites import despeckle, snap  # noqa: E402  (util pembersih + palet)

CELL_W, CELL_H = 40, 40
FOOT_Y = 38
AI_DIR = ROOT / "assets/character/frames/enemies"
OUT_DIR = ROOT / "game/assets/character"
DOCS = ROOT / "docs"

OUTL = (0x20, 0x20, 0x2E, 255)
CLEAR = (0, 0, 0, 0)

SLIME = [(0x2C, 0x5A, 0x2A, 255), (0x3F, 0x7A, 0x38, 255), (0x5F, 0xA0, 0x4A, 255), (0x86, 0xC0, 0x62, 255)]
SLIME_EYE = (0x1A, 0x1A, 0x24, 255)
BAT = [(0x2A, 0x24, 0x3A, 255), (0x44, 0x3B, 0x5C, 255), (0x5E, 0x52, 0x7C, 255)]
BAT_WING = (0x3A, 0x32, 0x50, 255)
BAT_EYE = (0xE8, 0xC8, 0x5A, 255)
FANG = (0xF2, 0xF0, 0xE6, 255)
AI_ANIMS = {"idle": ["idle_1", "idle_2"], "move": ["walk_1", "walk_2"],
            "attack": ["attack_1", "attack_2"], "hurt": ["hurt_1(1)"],
            "death": ["death_1", "death_2"]}
WARDEN_HEIGHT = 30


class Frame:
    def __init__(self, w: int = CELL_W, h: int = CELL_H):
        self.w, self.h = w, h
        self.px = [CLEAR] * (w * h)

    def set(self, x: int, y: int, c) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y * self.w + x] = c

    def get(self, x: int, y: int):
        return self.px[y * self.w + x] if 0 <= x < self.w and 0 <= y < self.h else CLEAR

    def filled(self, x: int, y: int) -> bool:
        return self.get(x, y)[3] != 0

    def rect(self, x, y, w, h, c) -> None:
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.set(xx, yy, c)

    def ellipse(self, cx, cy, rx, ry, c) -> None:
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                if rx <= 0 or ry <= 0:
                    continue
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                    self.set(x, y, c)

    def hline(self, x, y, w, c) -> None:
        for xx in range(x, x + w):
            self.set(xx, y, c)

    def outline(self, c=OUTL) -> None:
        edges = []
        for y in range(self.h):
            for x in range(self.w):
                if self.filled(x, y):
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    if self.filled(x + dx, y + dy):
                        edges.append((x, y))
                        break
        for x, y in edges:
            self.set(x, y, c)

    def to_image(self) -> Image.Image:
        im = Image.new("RGBA", (self.w, self.h))
        im.putdata(self.px)
        return im


# --------------------------------------------------------------------------- #
# slime
# --------------------------------------------------------------------------- #
def slime_frame(kind: str) -> Image.Image:
    f = Frame()
    cx = CELL_W // 2
    squash = {"idle_1": (7, 5), "idle_2": (8, 4), "move_1": (6, 7), "move_2": (8, 4),
              "attack": (7, 5), "hurt": (9, 3), "death_1": (10, 2), "death_2": (10, 2)}[kind]
    rx, ry = squash
    dx = 2 if kind == "attack" else 0
    cy = FOOT_Y - ry - 1
    f.ellipse(cx + dx, cy, rx, ry, SLIME[1])
    f.ellipse(cx + dx, cy - 1, rx - 1, ry - 1, SLIME[2])
    f.ellipse(cx + dx - 2, cy - 2, max(1, rx - 3), max(1, ry - 2), SLIME[3])
    f.ellipse(cx + dx, FOOT_Y - 1.5, rx - 0.5, 1.4, SLIME[0])   # alas gelap menempel di badan
    if kind != "death_2":
        eye_y = cy - 1
        f.rect(cx + dx - 3, eye_y, 2, 2, SLIME_EYE)
        f.rect(cx + dx + 1, eye_y, 2, 2, SLIME_EYE)
    if kind == "hurt":
        f.hline(cx + dx - 3, cy - 1, 7, SLIME_EYE)
    if kind == "death_1":
        f.rect(cx + dx - 3, cy - 1, 2, 1, SLIME_EYE)
        f.rect(cx + dx + 1, cy - 1, 2, 1, SLIME_EYE)
    f.outline()
    if kind == "death_2":
        px = f.px
        for i, c in enumerate(px):
            if c[3] > 0:
                px[i] = (int(c[0] * 0.6 + 90), int(c[1] * 0.6 + 90), int(c[2] * 0.6 + 90), 190)
        f.px = px
    return f.to_image()


# --------------------------------------------------------------------------- #
# kelelawar
# --------------------------------------------------------------------------- #
def bat_frame(kind: str) -> Image.Image:
    f = Frame()
    cx = CELL_W // 2
    cy = FOOT_Y - 12
    up = kind in ("idle_1", "move_1", "attack")
    body_h = 8
    f.ellipse(cx, cy, 3.4, body_h / 2, BAT[1])
    f.ellipse(cx - 1, cy - 1, 2.0, body_h / 2 - 1, BAT[2])
    for side in (-1, 1):
        for i in range(4):
            wx = cx + side * (4 + i * 2)
            wy = cy - (3 if up else 0) + (i if not up else -i // 2)
            f.rect(wx, wy, 2, 2, BAT_WING if i % 2 else BAT[2])
    ear_y = cy - body_h // 2 - 2
    f.set(cx - 2, ear_y, BAT[0])
    f.set(cx + 2, ear_y, BAT[0])
    eye_y = cy - 1
    if kind == "hurt":
        f.hline(cx - 2, eye_y, 5, BAT_EYE)
    else:
        f.set(cx - 1, eye_y, BAT_EYE)
        f.set(cx + 1, eye_y, BAT_EYE)
    if kind == "attack":
        f.set(cx - 1, cy + 3, FANG)
        f.set(cx + 1, cy + 3, FANG)
    if kind == "death_1":
        f.rect(cx - 4, FOOT_Y - 2, 9, 2, BAT[1])
    f.outline()
    if kind == "death_2":
        px = f.px
        for i, c in enumerate(px):
            if c[3] > 0:
                px[i] = (int(c[0] * 0.6 + 90), int(c[1] * 0.6 + 90), int(c[2] * 0.6 + 90), 170)
        f.px = px
    return f.to_image()


SLIME_ANIMS = {"idle": ["slime:idle_1", "slime:idle_2"], "move": ["slime:move_1", "slime:move_2"],
               "attack": ["slime:attack", "slime:attack"], "hurt": ["slime:hurt"],
               "death": ["slime:death_1", "slime:death_2"]}
BAT_ANIMS = {"idle": ["bat:idle_1", "bat:idle_2"], "move": ["bat:move_1", "bat:move_2"],
             "attack": ["bat:attack", "bat:attack"], "hurt": ["bat:hurt"],
             "death": ["bat:death_1", "bat:death_2"]}


def prepare_ai(path: Path, target_h: float) -> Image.Image:
    im = Image.open(path).convert("RGBA")
    box = im.getchannel("A").getbbox()
    if box is None:
        raise ValueError(f"{path} kosong")
    scale = target_h / (box[3] - box[1])
    nw, nh = max(1, round(im.width * scale)), max(1, round(im.height * scale))
    small = im.resize((nw, nh), Image.BOX)
    px = list(small.getdata())
    for i, (r, g, b, a) in enumerate(px):
        px[i] = (0, 0, 0, 0) if a < 110 else (*snap((r, g, b)), 255)
    despeckle(px, nw, nh)
    small.putdata(px)

    box = small.getchannel("A").getbbox()
    cell = Image.new("RGBA", (CELL_W, CELL_H), CLEAR)
    x0, y0, x1, y1 = box
    y_cut = y1 - max(1, (y1 - y0) // 4)
    total, acc = 0, 0
    p = small.load()
    for y in range(y_cut, y1):
        for x in range(x0, x1):
            if p[x, y][3] > 0:
                acc += x
                total += 1
    cx = acc / total if total else (x0 + x1) / 2
    cell.alpha_composite(small, (int(round(CELL_W // 2 - cx)), int(round(FOOT_Y + 1 - box[3]))))
    return cell


def build() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    DOCS.mkdir(parents=True, exist_ok=True)

    frames: dict[str, Image.Image] = {}
    anims: dict[str, dict] = {}
    for kind in ("idle_1", "idle_2", "move_1", "move_2", "attack", "hurt", "death_1", "death_2"):
        frames[f"slime:{kind}"] = slime_frame(kind)
    for kind in ("idle_1", "idle_2", "move_1", "move_2", "attack", "hurt", "death_1", "death_2"):
        frames[f"bat:{kind}"] = bat_frame(kind)

    anims["slime"] = {"sprite": "slime", "scale": 1.0, "animations": SLIME_ANIMS}
    anims["bat"] = {"sprite": "bat", "scale": 1.0, "animations": BAT_ANIMS}

    ai_anims: dict[str, list[str]] = {}
    missing: list[str] = []
    for anim, names in AI_ANIMS.items():
        keys = []
        for name in names:
            path = AI_DIR / f"{name}.png"
            if not path.exists():
                missing.append(name)
                continue
            key = f"warden:{name}"
            frames[key] = prepare_ai(path, WARDEN_HEIGHT)
            keys.append(key)
        ai_anims[anim] = keys
    anims["warden"] = {"sprite": "warden", "scale": 1.0, "animations": ai_anims}

    order = list(frames)
    columns = 8
    rows = (len(order) + columns - 1) // columns
    atlas = Image.new("RGBA", (columns * CELL_W, rows * CELL_H), CLEAR)
    index: dict[str, dict] = {}
    for i, key in enumerate(order):
        col, row = i % columns, i // columns
        atlas.paste(frames[key], (col * CELL_W, row * CELL_H))
        index[key] = {"col": col, "row": row, "x": col * CELL_W, "y": row * CELL_H,
                      "w": CELL_W, "h": CELL_H}
    atlas.save(OUT_DIR / "enemy_atlas.png")
    meta = {
        "cell": {"w": CELL_W, "h": CELL_H},
        "foot": {"x": CELL_W // 2, "y": FOOT_Y},
        "atlas": {"file": "enemy_atlas.png", "columns": columns, "rows": rows,
                  "width": atlas.width, "height": atlas.height},
        "frame_count": len(order),
        "frames": index,
        "enemies": anims,
        "missing_from_ai": missing,
        "source": "tools/build_enemy_sprites.py",
    }
    (OUT_DIR / "enemy.json").write_text(json.dumps(meta, indent=2) + "\n")

    tiles = json.loads((ROOT / "game/assets/tiles/tileset.json").read_text())
    atlas_tiles = Image.open(ROOT / "game/assets/tiles/tileset.png").convert("RGBA")
    g = tiles["tiles"]["grass_1"]
    grass = atlas_tiles.crop((g["x"], g["y"], g["x"] + 16, g["y"] + 16))
    preview = Image.new("RGBA", (atlas.width * 4, atlas.height * 4 + 16), (0x22, 0x22, 0x2A, 255))
    bg = Image.new("RGBA", atlas.size)
    for ty in range(0, atlas.height, 16):
        for tx in range(0, atlas.width, 16):
            bg.alpha_composite(grass, (tx, ty))
    preview.alpha_composite(bg.resize((atlas.width * 4, atlas.height * 4), Image.NEAREST), (0, 16))
    preview.alpha_composite(atlas.resize((atlas.width * 4, atlas.height * 4), Image.NEAREST), (0, 16))
    d = ImageDraw.Draw(preview)
    d.text((4, 3), f"enemy atlas - {len(order)} frames, cell {CELL_W}x{CELL_H}", fill=(255, 255, 255))
    for i, key in enumerate(order):
        col, row = i % columns, i // columns
        d.text((col * CELL_W * 4 + 2, 18 + row * CELL_H * 4), key, fill=(0xE8, 0xC8, 0x5A, 255))
    preview.convert("RGB").save(DOCS / "preview-enemies.png")

    print(f"enemy_atlas.png: {atlas.width}x{atlas.height} ({len(order)} frame)")
    print(f"  prosedural : slime, bat ({len([k for k in order if not k.startswith('warden')])} frame)")
    print(f"  AI (boss)  : warden ({len([k for k in order if k.startswith('warden')])} frame)")
    if missing:
        print(f"  CATATAN: frame AI tidak ada -> {', '.join(missing)}")
    print(f"preview: {DOCS / 'preview-enemies.png'}")


if __name__ == "__main__":
    build()
