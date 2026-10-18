#!/usr/bin/env python3
"""Generator frame hero top-down 4 arah (gaya SNES JRPG 3/4 view).

Digambar prosedural dengan palet + proporsi yang sama dengan Arga
(PROMPTS.md), jadi animasi 4 arah konsisten (tanpa jitter antar frame) dan
cocok ditempel berdampingan dengan frame AI yang sudah ada.

Yang dihasilkan: per arah (down, up, left, right) animasi
  idle (2 frame), walk (4 frame), run (4 frame)
digambar pada sel frame CELL_W x CELL_H dengan telapak kaki di (CX, FOOT_Y).

Jalankan:
    python3 tools/generate_topdown_frames.py
"""

from __future__ import annotations

import json
import math
from pathlib import Path

from PIL import Image

# --------------------------------------------------------------------------- #
# palet (identik dengan PROMPTS.md / tools/generate_character_frames.py)
# --------------------------------------------------------------------------- #
SKIN = (0xF2, 0xC9, 0xA0, 255)
SKIN_S = (0xD1, 0xA2, 0x76, 255)
SKIN_H = (0xFF, 0xE4, 0xC4, 255)
HAIR = (0x7A, 0x4A, 0x26, 255)
HAIR_S = (0x5A, 0x34, 0x19, 255)
HAIR_H = (0x9C, 0x65, 0x34, 255)
TUNIC = (0x4A, 0x6F, 0xA5, 255)
TUNIC_S = (0x35, 0x56, 0x7D, 255)
TUNIC_H = (0x6B, 0x93, 0xC9, 255)
ARMOR = (0xB9, 0xC3, 0xCF, 255)
ARMOR_S = (0x7F, 0x8B, 0x98, 255)
ARMOR_H = (0xE2, 0xE8, 0xF0, 255)
LEATHER = (0x8A, 0x5A, 0x30, 255)
LEATHER_S = (0x5E, 0x3D, 0x20, 255)
LEATHER_H = (0xA9, 0x78, 0x45, 255)
BLADE = (0xDD, 0xE4, 0xEC, 255)
BLADE_S = (0xA8, 0xB4, 0xC2, 255)
OUTLINE = (0x20, 0x20, 0x2E, 255)
CLEAR = (0, 0, 0, 0)

CELL_W, CELL_H = 64, 40
CX, FOOT_Y = 32, 36

# metrik tubuh: tinggi total 22 px
HEAD_TOP = FOOT_Y - 22          # 14
HEAD_H = 8
BODY_W = 11
BODY_X = CX - BODY_W // 2       # 27 (front/back)
TORSO_TOP = HEAD_TOP + HEAD_H   # 22
LEG_TOP = TORSO_TOP + 9         # 31
LEG_H = FOOT_Y - LEG_TOP        # 5
ARM_H = 7                        # tangan berhenti di garis pinggang, bukan di tanah

DIRECTIONS = ("down", "up", "left", "right")
ANIMATIONS = {"idle": 2, "walk": 4, "run": 4}


class Frame:
    def __init__(self, w: int = CELL_W, h: int = CELL_H):
        self.w, self.h = w, h
        self.px = [CLEAR] * (w * h)

    def get(self, x: int, y: int):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.px[y * self.w + x]
        return CLEAR

    def set(self, x: int, y: int, c) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y * self.w + x] = c

    def filled(self, x: int, y: int) -> bool:
        return self.get(x, y)[3] != 0

    def rect(self, x: int, y: int, w: int, h: int, c) -> None:
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.set(xx, yy, c)

    def vline(self, x: int, y: int, h: int, c) -> None:
        for yy in range(y, y + h):
            self.set(x, yy, c)

    def hline(self, x: int, y: int, w: int, c) -> None:
        for xx in range(x, x + w):
            self.set(xx, y, c)

    def outline(self, c=OUTLINE) -> None:
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
# bagian tubuh
# --------------------------------------------------------------------------- #
def draw_leg(f: Frame, x: int, y_bottom: int, w: int, lift: int, far: bool = False) -> None:
    """Kaki + sepatu boot. lift = px terangkat dari tanah."""
    boot = LEATHER_S if far else LEATHER
    pant = TUNIC_S if far else TUNIC
    y0 = y_bottom - LEG_H - lift
    f.rect(x, y0, w, 2, pant)
    f.rect(x, y0 + 2, w, LEG_H - 3, boot)
    f.rect(x, y_bottom - 1 - lift, w, 1, boot)
    f.hline(x, y0, w, TUNIC_S if far else TUNIC_H)


def draw_arm(f: Frame, x: int, y_top: int, swing: int, w: int = 3) -> None:
    """Lengan (tunic) dengan tangan (skin); swing = px geser vertikal telapak."""
    f.rect(x, y_top, w, 4, TUNIC)
    f.vline(x, y_top, 4, TUNIC_H)
    fore_h = max(2, ARM_H - 4 - max(0, -swing))
    f.rect(x, y_top + 4, w, fore_h, TUNIC_S)
    hand_y = y_top + 4 + fore_h + swing
    f.rect(x, hand_y, w, 2, SKIN)
    f.set(x, hand_y, SKIN_H)


def draw_sword_side(f: Frame, hx: int, hy: int, angle_deg: float, length: int = 7) -> None:
    """Pedang dari titik tangan, 0 derajat = bawah, positif = ke depan (kanan)."""
    rad = math.radians(angle_deg)
    dx, dy = math.sin(rad), math.cos(rad)
    for i in range(-2, 0):
        f.set(int(round(hx + dx * i)), int(round(hy + dy * i)), LEATHER_S)
    gx, gy = int(round(hx + dx * 1)), int(round(hy + dy * 1))
    f.set(gx, gy, ARMOR_S)
    f.set(gx + 1, gy, ARMOR_S)
    for i in range(1, length):
        x, y = int(round(hx + dx * i)), int(round(hy + dy * i))
        f.set(x, y, BLADE if i % 3 else BLADE_S)
        f.set(x + 1, y, BLADE_S)


def draw_sword_back(f: Frame, side: int = 1) -> None:
    """Hulu + bilah pedang menyilang di punggung (untuk view depan/belakang)."""
    for i in range(5):
        f.set(CX + side * (2 + i // 2) , HEAD_TOP + 9 - i, BLADE_S if i % 2 else BLADE)
    f.set(CX + side * 2, HEAD_TOP + 10, ARMOR)
    f.set(CX + side * 3, HEAD_TOP + 10, ARMOR_H)


def draw_head_front(f: Frame, bob: int, lean: int) -> None:
    top = HEAD_TOP + bob
    f.rect(BODY_X, top, BODY_W, HEAD_H, SKIN)
    # rambut: atas + sisi, sedikit miring sesuai arah hadap
    f.rect(BODY_X, top, BODY_W, 3, HAIR)
    f.hline(BODY_X, top, BODY_W, HAIR_H)
    f.rect(BODY_X, top + 3, 1, 3, HAIR)
    f.rect(BODY_X + BODY_W - 1, top + 3, 1, 3, HAIR_S)
    f.rect(BODY_X + 1, top + 3, 2, 1, HAIR_S)
    # wajah
    eye_y = top + 4
    f.rect(BODY_X + 2 + lean, eye_y, 2, 2, OUTLINE)
    f.rect(BODY_X + BODY_W - 4 + lean, eye_y, 2, 2, OUTLINE)
    f.set(BODY_X + 1 + lean, eye_y + 3, SKIN_S)
    f.set(BODY_X + BODY_W - 2 + lean, eye_y + 3, SKIN_S)
    f.hline(BODY_X + 4 + lean, top + 6, 3, SKIN_S)   # mulut
    f.hline(BODY_X + 1, top + 1, 3, SKIN_H)          # highlight wajah


def draw_head_back(f: Frame, bob: int) -> None:
    top = HEAD_TOP + bob
    f.rect(BODY_X, top, BODY_W, HEAD_H, HAIR)
    f.hline(BODY_X, top, BODY_W, HAIR_H)
    f.hline(BODY_X, top + 1, BODY_W - 2, HAIR_H)
    f.rect(BODY_X, top + 5, BODY_W, 3, HAIR_S)
    f.rect(BODY_X + 2, top + 7, 2, 1, SKIN_S)        # leher
    f.rect(BODY_X + BODY_W - 3, top + 7, 2, 1, SKIN_S)


# --------------------------------------------------------------------------- #
# view depan (down) & belakang (up)
# --------------------------------------------------------------------------- #
def draw_front(f: Frame, pose: dict, back: bool = False) -> None:
    bob = pose["bob"]
    leg_l = pose["leg"][0]
    leg_r = pose["leg"][1]
    body_y = TORSO_TOP + bob

    draw_leg(f, BODY_X, FOOT_Y, 4, leg_l[1])
    draw_leg(f, BODY_X + BODY_W - 4, FOOT_Y, 4, leg_r[1])

    # lengan (kiri/kanan) + tangan
    draw_arm(f, BODY_X - 3, body_y + 1, pose["arm"][0])
    draw_arm(f, BODY_X + BODY_W, body_y + 1, pose["arm"][1])

    # torso
    f.rect(BODY_X, body_y, BODY_W, 9, TUNIC)
    f.hline(BODY_X, body_y, BODY_W, TUNIC_H)
    f.vline(BODY_X + BODY_W - 1, body_y, 9, TUNIC_S)
    f.vline(BODY_X + BODY_W - 2, body_y + 1, 7, TUNIC_S)
    f.rect(BODY_X + 1, body_y + 7, BODY_W - 2, 2, LEATHER)      # ikat pinggang
    f.hline(BODY_X + 1, body_y + 7, BODY_W - 2, LEATHER_H)
    f.set(BODY_X + 2, body_y + 8, LEATHER_S)                     # kantong kecil
    if back:
        f.vline(BODY_X + BODY_W // 2, body_y + 1, 6, TUNIC_S)    # jahitan punggung
        f.rect(BODY_X + 1, body_y + 1, BODY_W - 2, 1, TUNIC_S)
    else:
        f.rect(BODY_X + 3, body_y + 3, BODY_W - 6, 1, TUNIC_H)   # lipatan kain
    # pauldron bahu kanan (dari sudut pandang pemain)
    f.rect(BODY_X - 1, body_y, 4, 3, ARMOR)
    f.hline(BODY_X - 1, body_y, 4, ARMOR_H)
    f.vline(BODY_X + 2, body_y, 3, ARMOR_S)

    if back:
        draw_head_back(f, bob)
        draw_sword_back(f, -1)
    else:
        draw_head_front(f, bob, pose.get("head_lean", 0))
        draw_sword_back(f, 1)
    f.outline()


# --------------------------------------------------------------------------- #
# view samping (left/right)
# --------------------------------------------------------------------------- #
SIDE_W = 8
SIDE_X = CX - SIDE_W // 2 - 1


def draw_side(f: Frame, pose: dict, facing: int) -> None:
    """facing = +1 menghadap kanan, -1 menghadap kiri (semua digambar menghadap kanan, lalu mirror)."""
    bob = pose["bob"]
    body_y = TORSO_TOP + bob
    far = pose["far_leg"]
    near = pose["near_leg"]

    draw_leg(f, SIDE_X - far[0], FOOT_Y, 4, far[1], far=True)
    # lengan belakang (lebih gelap, sedikit di belakang badan)
    draw_arm(f, SIDE_X - 2, body_y + 2, pose["far_arm"], 3)
    draw_leg(f, SIDE_X + near[0], FOOT_Y, 4, near[1])

    # torso menyamping (lebih sempit)
    f.rect(SIDE_X, body_y, SIDE_W, 9, TUNIC)
    f.hline(SIDE_X, body_y, SIDE_W, TUNIC_H)
    f.vline(SIDE_X + SIDE_W - 1, body_y, 9, TUNIC_S)
    f.rect(SIDE_X, body_y + 7, SIDE_W, 2, LEATHER)
    f.hline(SIDE_X, body_y + 7, SIDE_W, LEATHER_H)
    f.set(SIDE_X + SIDE_W - 3, body_y + 8, LEATHER_S)
    # pauldron
    f.rect(SIDE_X + SIDE_W - 5, body_y, 4, 3, ARMOR)
    f.hline(SIDE_X + SIDE_W - 5, body_y, 4, ARMOR_H)

    # kepala: profil dengan hidung
    top = HEAD_TOP + bob
    f.rect(SIDE_X - 1, top, SIDE_W + 2, HEAD_H, SKIN)
    f.rect(SIDE_X - 1, top, SIDE_W + 2, 3, HAIR)
    f.hline(SIDE_X - 1, top, SIDE_W + 1, HAIR_H)
    f.rect(SIDE_X - 2, top + 3, 2, 2, HAIR)          # rambut belakang
    f.set(SIDE_X + SIDE_W, top + 4, SKIN)             # hidung
    f.rect(SIDE_X + SIDE_W - 3, top + 4, 2, 2, OUTLINE)  # mata profil
    f.set(SIDE_X + SIDE_W - 4, top + 6, SKIN_S)

    # lengan depan + pedang (pedang digambar sebelum lengan supaya gagang di tangan)
    front_arm = pose["near_arm"]
    arm_x = SIDE_X + SIDE_W - 4
    hand_y = body_y + 1 + 4 + max(2, ARM_H - 4 - max(0, -front_arm)) + front_arm + 1
    draw_sword_side(f, arm_x + 1, hand_y, pose["sword"])
    draw_arm(f, arm_x, body_y + 1, front_arm, 3)

    f.outline()
    if facing < 0:
        mirror(f)


def mirror(f: Frame) -> None:
    for y in range(f.h):
        row = f.px[y * f.w:(y + 1) * f.w]
        f.px[y * f.w:(y + 1) * f.w] = row[::-1]


# --------------------------------------------------------------------------- #
# tabel pose
# --------------------------------------------------------------------------- #
def P(bob=0, leg_l=(0, 0), leg_r=(0, 0), arm_l=0, arm_r=0, head_lean=0,
      near_leg=(0, 0), far_leg=(0, 0), near_arm=0, far_arm=0, sword=25) -> dict:
    return dict(bob=bob, leg=[leg_l, leg_r], arm=(arm_l, arm_r), head_lean=head_lean,
                near_leg=near_leg, far_leg=far_leg, near_arm=near_arm, far_arm=far_arm, sword=sword)


POSES: dict[str, dict[str, dict]] = {
    "down": {
        "idle_1": P(),
        "idle_2": P(bob=1, arm_l=0, arm_r=0, head_lean=0),
        "walk_1": P(bob=0, leg_l=(0, 2), leg_r=(0, 0), arm_l=-1, arm_r=1),
        "walk_2": P(bob=-1, leg_l=(0, 0), leg_r=(0, 1), arm_l=0, arm_r=0),
        "walk_3": P(bob=0, leg_l=(0, 0), leg_r=(0, 2), arm_l=1, arm_r=-1),
        "walk_4": P(bob=-1, leg_l=(0, 1), leg_r=(0, 0), arm_l=0, arm_r=0),
        "run_1": P(bob=0, leg_l=(0, 3), leg_r=(0, 0), arm_l=-2, arm_r=2, head_lean=0),
        "run_2": P(bob=-2, leg_l=(0, 1), leg_r=(0, 1), arm_l=0, arm_r=0),
        "run_3": P(bob=0, leg_l=(0, 0), leg_r=(0, 3), arm_l=2, arm_r=-2),
        "run_4": P(bob=-2, leg_l=(0, 1), leg_r=(0, 1), arm_l=0, arm_r=0),
    },
    "up": {
        "idle_1": P(),
        "idle_2": P(bob=1),
        "walk_1": P(bob=0, leg_l=(0, 0), leg_r=(0, 2), arm_l=1, arm_r=-1),
        "walk_2": P(bob=-1, leg_l=(0, 1), leg_r=(0, 0), arm_l=0, arm_r=0),
        "walk_3": P(bob=0, leg_l=(0, 2), leg_r=(0, 0), arm_l=-1, arm_r=1),
        "walk_4": P(bob=-1, leg_l=(0, 0), leg_r=(0, 1), arm_l=0, arm_r=0),
        "run_1": P(bob=0, leg_l=(0, 0), leg_r=(0, 3), arm_l=2, arm_r=-2),
        "run_2": P(bob=-2, leg_l=(0, 1), leg_r=(0, 1), arm_l=0, arm_r=0),
        "run_3": P(bob=0, leg_l=(0, 3), leg_r=(0, 0), arm_l=-2, arm_r=2),
        "run_4": P(bob=-2, leg_l=(0, 1), leg_r=(0, 1), arm_l=0, arm_r=0),
    },
    "side": {
        "idle_1": P(near_leg=(0, 0), far_leg=(-1, 0), sword=52),
        "idle_2": P(bob=1, near_leg=(0, 0), far_leg=(-1, 0), sword=54),
        "walk_1": P(bob=0, near_leg=(2, 0), far_leg=(-2, 0), near_arm=1, far_arm=-1, sword=58),
        "walk_2": P(bob=-1, near_leg=(1, 1), far_leg=(-1, 0), near_arm=0, far_arm=0, sword=52),
        "walk_3": P(bob=0, near_leg=(-2, 0), far_leg=(2, 0), near_arm=-1, far_arm=1, sword=44),
        "walk_4": P(bob=-1, near_leg=(-1, 0), far_leg=(1, 1), near_arm=0, far_arm=0, sword=50),
        "run_1": P(bob=0, near_leg=(3, 1), far_leg=(-3, 0), near_arm=2, far_arm=-2, sword=72),
        "run_2": P(bob=-2, near_leg=(1, 2), far_leg=(-1, 1), near_arm=0, far_arm=0, sword=66),
        "run_3": P(bob=0, near_leg=(-3, 0), far_leg=(3, 1), near_arm=-2, far_arm=2, sword=62),
        "run_4": P(bob=-2, near_leg=(-1, 1), far_leg=(1, 2), near_arm=0, far_arm=0, sword=66),
    },
}


def render(direction: str, anim: str, index: int) -> Image.Image:
    key = f"{anim}_{index + 1}"
    f = Frame()
    if direction in ("down", "up"):
        draw_front(f, POSES[direction][key], back=(direction == "up"))
    else:
        draw_side(f, POSES["side"][key], 1 if direction == "right" else -1)
    return f.to_image()


def all_frames() -> dict[str, list[Image.Image]]:
    out: dict[str, list[Image.Image]] = {}
    for d in DIRECTIONS:
        for anim, count in ANIMATIONS.items():
            out[f"{d}_{anim}"] = [render(d, anim, i) for i in range(count)]
    return out


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    out_dir = root / "assets/character/topdown"
    out_dir.mkdir(parents=True, exist_ok=True)
    frames = all_frames()
    total = 0
    for key, imgs in frames.items():
        for i, im in enumerate(imgs):
            im.save(out_dir / f"{key}_{i + 1}.png")
            total += 1
    meta = {
        "cell": {"w": CELL_W, "h": CELL_H},
        "foot": {"x": CX, "y": FOOT_Y},
        "directions": list(DIRECTIONS),
        "animations": {k: v for k, v in ANIMATIONS.items()},
        "frames": total,
        "source": "tools/generate_topdown_frames.py",
        "keys": sorted(frames),
    }
    (out_dir / "topdown.json").write_text(json.dumps(meta, indent=2) + "\n")
    print(f"{total} frame top-down -> {out_dir} ({len(frames)} animasi arah)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
