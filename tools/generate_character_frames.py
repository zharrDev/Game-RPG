#!/usr/bin/env python3
"""Generate 23 frame karakter "Arga, Young Warrior Knight" secara prosedural.

Karakter digambar dari satu spesifikasi kerangka (skeleton) + tabel pose, memakai
palet warna persis dari PROMPTS.md. Karena prosedural, semua frame dijamin
konsisten (tanpa jitter seperti hasil AI image generator).

Output:
  assets/character/frames/<nama>.png    23 frame, kanvas 88x88 px, background transparan
  assets/character/generated/anchor.json  titik kaki (px) di dalam kanvas -> dipakai engine
  assets/character/reference/hero-reference.png  render 4x idle_1 di background magenta

Semua frame memakai kanvas berukuran SAMA dan karakter diletakkan di posisi yang
konsisten, jadi tools/make_spritesheet.py harus dijalankan dengan `align: canvas`
(lihat character.json) supaya lompatan/posisi tiap frame tetap benar.

Jalankan:
    python3 tools/generate_character_frames.py
"""

from __future__ import annotations

import json
import math
from pathlib import Path

from PIL import Image

# --------------------------------------------------------------------------- #
# palet (PROMPTS.md section 1)
# --------------------------------------------------------------------------- #
SKIN = (0xF2, 0xC9, 0xA0, 255)
SKIN_S = (0xD1, 0xA2, 0x76, 255)
SKIN_H = (0xFF, 0xE4, 0xC4, 255)
HAIR = (0x7A, 0x4A, 0x26, 255)
HAIR_S = (0x5A, 0x34, 0x19, 255)
TUNIC = (0x4A, 0x6F, 0xA5, 255)
TUNIC_S = (0x35, 0x56, 0x7D, 255)
TUNIC_H = (0x6B, 0x93, 0xC9, 255)
ARMOR = (0xB9, 0xC3, 0xCF, 255)
ARMOR_S = (0x7F, 0x8B, 0x98, 255)
ARMOR_H = (0xE2, 0xE8, 0xF0, 255)
LEATHER = (0x8A, 0x5A, 0x30, 255)
LEATHER_S = (0x5E, 0x3D, 0x20, 255)
BLADE = (0xDD, 0xE4, 0xEC, 255)
BLADE_S = (0xA8, 0xB4, 0xC2, 255)
BLADE_H = (0xFF, 0xFF, 0xFF, 255)
OUTLINE = (0x20, 0x20, 0x2E, 255)
MAGENTA = (255, 0, 255, 255)
GLOW_A = (0x8F, 0xE3, 0xFF)  # aura skill
GLOW_B = (0xFF, 0xD7, 0x6A)
WISP = (0xC9, 0xE6, 0xFF)

# --------------------------------------------------------------------------- #
# geometri (satuan "piksel logis", 1 unit = 2px output -> piksel chunky)
# --------------------------------------------------------------------------- #
UNIT = 2
LOGICAL_W, LOGICAL_H = 44, 48
GROUND_ROW = 41  # baris bawah telapak kaki untuk frame yang menapak (0-indexed)
HIP = (20.0, 26.0)
TORSO_LEN = 10.0
THIGH, SHIN = 6.0, 6.0
UPPER_ARM, FOREARM = 5.0, 5.0
BLADE_LEN = 11.0  # ujung pedang pas menyentuh garis tanah pada pose berdiri


def vec(angle_deg: float) -> tuple[float, float]:
    """0 derajat = bawah, 90 = depan (kanan), 180 = atas, -90 = belakang."""
    rad = math.radians(angle_deg)
    return math.sin(rad), math.cos(rad)


def add(p, d, k=1.0):
    return (p[0] + d[0] * k, p[1] + d[1] * k)


def blend(dst, src):
    """Alpha-blend src (rgb, a) di atas dst (rgba)."""
    sr, sg, sb = src[0][0], src[0][1], src[0][2]
    sa = src[1] / 255.0
    dr, dg, db, da = dst
    if da == 0:
        return (sr, sg, sb, int(255 * sa))
    na = sa + (da / 255.0) * (1 - sa)
    if na <= 0:
        return (0, 0, 0, 0)
    return (
        int((sr * sa + dr * (da / 255.0) * (1 - sa)) / na),
        int((sg * sa + dg * (da / 255.0) * (1 - sa)) / na),
        int((sb * sa + db * (da / 255.0) * (1 - sa)) / na),
        int(na * 255),
    )


class Canvas:
    """Kanvas piksel logis berisi RGBA."""

    def __init__(self, w: int = LOGICAL_W, h: int = LOGICAL_H):
        self.w, self.h = w, h
        self.px: list[list[tuple[int, int, int, int]]] = [[(0, 0, 0, 0)] * w for _ in range(h)]

    def get(self, x: int, y: int):
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.px[y][x]
        return (0, 0, 0, 0)

    def set(self, x: int, y: int, color) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y][x] = color

    def blend(self, x: int, y: int, rgb, alpha: int) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y][x] = blend(self.px[y][x], (rgb, alpha))

    def dot(self, pos: tuple[float, float], color, size: int = 1) -> None:
        """Stamp kotak size x size (koordinat dibulatkan -> gaya piksel art)."""
        x0, y0 = int(round(pos[0])), int(round(pos[1]))
        for y in range(y0, y0 + size):
            for x in range(x0, x0 + size):
                self.set(x, y, color)

    def rect(self, x0: int, y0: int, x1: int, y1: int, color) -> None:
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, color)

    def line(self, p0, p1, thickness: int, color) -> None:
        """Garis tebal bergaya piksel art (stamp di sepanjang segmen)."""
        dx, dy = p1[0] - p0[0], p1[1] - p0[1]
        length = max(1.0, math.hypot(dx, dy))
        steps = int(length * 2) + 1
        for i in range(steps + 1):
            t = i / steps
            self.dot((p0[0] + dx * t, p0[1] + dy * t), color, thickness)

    def visible(self) -> set[tuple[int, int]]:
        return {(x, y) for y in range(self.h) for x in range(self.w) if self.px[y][x][3] > 0}

    def to_image(self, unit: int = UNIT) -> Image.Image:
        img = Image.new("RGBA", (self.w * unit, self.h * unit), (0, 0, 0, 0))
        pixels = img.load()
        for y in range(self.h):
            for x in range(self.w):
                c = self.px[y][x]
                if c[3] == 0:
                    continue
                for dy in range(unit):
                    for dx in range(unit):
                        pixels[x * unit + dx, y * unit + dy] = c
        return img


def with_outline(art: Canvas, glow: Canvas | None = None) -> Canvas:
    """Tambah outline gelap 1px di sekeliling siluet (gaya 16-bit)."""
    sil = art.visible()
    out = Canvas(art.w, art.h)
    for x, y in sil:
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                nx, ny = x + dx, y + dy
                if (nx, ny) not in sil:
                    out.set(nx, ny, OUTLINE)
    for y in range(art.h):
        for x in range(art.w):
            c = art.px[y][x]
            if c[3] > 0:
                out.set(x, y, c)
    if glow is not None:
        for y in range(glow.h):
            for x in range(glow.w):
                c = glow.px[y][x]
                if c[3] > 0:
                    out.px[y][x] = blend(out.px[y][x], ((c[0], c[1], c[2]), c[3]))
    return out


# --------------------------------------------------------------------------- #
# gambar karakter
# --------------------------------------------------------------------------- #
def draw_sword(c: Canvas, hand, angle: float, faded: bool = False) -> None:
    d = vec(angle)
    perp = vec(angle + 90)
    blade_color = BLADE if not faded else tuple(int(v * 0.8 + 40) for v in BLADE[:3]) + (255,)
    c.line(hand, add(hand, d, BLADE_LEN), 1, blade_color)
    highlight_mid = add(hand, d, BLADE_LEN * 0.75)
    c.line(add(hand, d, BLADE_LEN * 0.35), highlight_mid, 1, BLADE_H)
    tip = add(hand, d, BLADE_LEN)
    c.dot(tip, BLADE_S)
    guard_c = add(hand, d, 2)
    c.line(add(guard_c, perp, 2), add(guard_c, perp, -2), 1, BLADE_S)
    c.line(add(hand, d, -3), hand, 1, LEATHER)
    c.dot(add(hand, d, -4), LEATHER_S)


def draw_limb(c: Canvas, origin, a1: float, a2: float, l1: float, l2: float, color, thickness: int, boot=None):
    mid = add(origin, vec(a1), l1)
    tip = add(mid, vec(a1 + a2), l2)
    c.line(origin, mid, thickness, color)
    c.line(mid, tip, thickness, color)
    if boot is not None:
        c.line(tip, add(tip, (1, 0), 0), thickness, boot)  # jari kaki ke depan
        c.dot(tip, boot, thickness)
        c.dot(add(tip, vec(90), 1.5), boot, thickness)
    return tip


def leg_reach(angles: tuple[float, float]) -> float:
    """Jarak vertikal hip -> telapak kaki untuk satu kaki (satuan logis)."""
    thigh, knee = angles
    return math.cos(math.radians(thigh)) * THIGH + math.cos(math.radians(thigh + knee)) * SHIN


def render_pose(pose: dict) -> tuple[Canvas, Canvas]:
    art = Canvas()
    glow = Canvas()
    hip_dx, hip_dy = pose.get("hip", (0, 0))
    # Auto-plant: geser seluruh tubuh supaya kaki terendah tepat di GROUND_ROW,
    # lalu angkat sebesar `lift` untuk frame yang sedang di udara (lompat/lari).
    reach = max(leg_reach(pose["legF"]), leg_reach(pose["legB"]))
    planted_bottom = int(round(HIP[1] + hip_dy + reach)) + 1  # baris bawah boot
    shift = GROUND_ROW - int(pose.get("lift", 0)) - planted_bottom
    hip = (HIP[0] + hip_dx, HIP[1] + hip_dy + shift)
    lean = pose.get("lean", 0)
    head_lean = pose.get("head", 0)
    leg_f, leg_b = pose["legF"], pose["legB"]
    arm_f, arm_b = pose["armF"], pose["armB"]

    up = vec(180 + lean)  # arah tulang punggung (atas, condong)
    fwd = vec(90 + lean)  # arah depan
    neck = add(hip, up, TORSO_LEN)
    shoulder_hip = add(hip, up, TORSO_LEN * 0.8)
    sh_f = add(shoulder_hip, fwd, 2)
    sh_b = add(shoulder_hip, fwd, -2)

    # --- kaki belakang
    draw_limb(art, hip, leg_b[0], leg_b[1], THIGH, SHIN, TUNIC_S, 2, LEATHER_S)
    # --- lengan belakang
    draw_limb(art, sh_b, arm_b[0], arm_b[1], UPPER_ARM, FOREARM, TUNIC_S, 2, SKIN_S)
    # --- torso + belt + pauldron
    art.line(hip, neck, 6, TUNIC)
    art.line(add(hip, fwd, -2), add(neck, fwd, -2), 2, TUNIC_S)  # bayangan sisi belakang
    art.line(add(hip, up, 2.0), add(neck, up, -1), 6, LEATHER_S) if False else None
    belt = add(hip, up, 2)
    art.line(add(belt, fwd, 2.6), add(belt, fwd, -2.6), 2, LEATHER)
    art.dot(add(belt, fwd, 1.8), LEATHER, 2)  # pouch kecil
    art.line(add(shoulder_hip, up, -1), add(shoulder_hip, up, 2.5), 5, TUNIC_H)  # highlight dada
    art.dot(add(sh_f, fwd, 0.6), ARMOR, 3)  # pauldron bahu depan
    art.dot(add(sh_f, up, 1.6), ARMOR_H, 2)
    # --- kepala
    head_top = add(neck, vec(180 + lean + head_lean), 6)
    art.line(neck, head_top, 5, SKIN)
    art.line(add(head_top, vec(180 + lean + head_lean), -1), head_top, 5, HAIR)
    art.line(add(head_top, fwd, -1.8), add(head_top, fwd, 0.6), 2, HAIR)
    art.line(add(head_top, vec(180 + lean + head_lean), -1.5), add(head_top, vec(180 + lean + head_lean), -0.5), 5, HAIR_S)
    art.dot(add(head_top, fwd, 1.6), SKIN_H, 2)  # highlight wajah
    eye = add(add(head_top, fwd, 1.4), vec(180 + lean + head_lean), 1.5)
    art.dot(eye, OUTLINE)
    # --- kaki depan
    draw_limb(art, hip, leg_f[0], leg_f[1], THIGH, SHIN, TUNIC, 2, LEATHER)
    # --- lengan depan + pedang
    hand = draw_limb(art, sh_f, arm_f[0], arm_f[1], UPPER_ARM, FOREARM, TUNIC, 2, SKIN)
    draw_sword(art, hand, pose.get("sword", 0))

    # --- aura skill
    intensity = pose.get("glow", 0.0)
    if intensity > 0:
        d = vec(pose.get("sword", 0))
        for i in range(int(BLADE_LEN) + 3):
            p = add(hand, d, i)
            glow.blend(int(p[0]), int(p[1]), GLOW_A, int(70 * intensity))
            glow.blend(int(p[0]) + 1, int(p[1]), GLOW_B, int(50 * intensity))
        radius = 4 + int(10 * intensity)
        for (dx, dy) in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            for r in range(2, radius):
                p = (int(hand[0]) + dx * r, int(hand[1]) + dy * r)
                glow.blend(p[0], p[1], GLOW_B if intensity > 0.6 else GLOW_A, max(0, int(120 * intensity) - r * 10))
    return art, glow


def render_prone(fade: float) -> tuple[Canvas, Canvas]:
    """Pose telentang di tanah untuk frame death_2 / death_3."""
    art = Canvas()
    glow = Canvas()
    tone = (lambda c: tuple(int(v * (1 - fade) + 200 * fade) for v in c[:3]) + (255,))
    alpha = int(255 - fade * 90)
    ground = GROUND_ROW - 4  # badan tergeletak: baris GROUND_ROW-4 .. GROUND_ROW
    # kaki (menjulur ke kiri)
    art.line((13, ground), (7, ground + 1), 3, tone(TUNIC))
    art.line((13, ground + 1), (8, ground + 2), 3, tone(TUNIC_S))
    art.dot((6, ground + 1), tone(LEATHER), 3)
    art.dot((7, ground + 2), tone(LEATHER_S), 3)
    # torso
    art.line((12, ground), (24, ground), 5, tone(TUNIC))
    art.line((12, ground + 2), (24, ground + 2), 2, tone(TUNIC_S))
    art.line((13, ground + 1), (23, ground + 1), 2, tone(LEATHER))
    # kepala (terlentang, menghadap kanan)
    art.line((24, ground - 1), (30, ground - 1), 6, tone(SKIN))
    art.line((26, ground - 1), (31, ground - 1), 6, tone(HAIR))
    art.dot((31, ground - 2), tone(HAIR_S), 2)
    eye = (28, ground - 1)
    for dy in (0, -2):
        art.dot((eye[0], eye[1] + dy), tone(OUTLINE), 2)  # mata terpejam
    # lengan terentang
    art.line((22, ground - 1), (27, ground - 4), 2, tone(SKIN))
    art.line((18, ground - 1), (14, ground - 4), 2, tone(SKIN_S))
    # pedang tergeletak di tanah, di depan (kanan) badan
    art.line((16, ground + 3), (28, ground + 3), 1, tone(BLADE))
    art.line((20, ground + 3), (26, ground + 3), 1, tone(BLADE_H))
    art.line((16, ground + 1), (16, ground + 3), 1, tone(LEATHER))
    if fade > 0:
        for i, x in enumerate((10, 16, 22, 27)):
            for k in range(4):
                glow.blend(x + (i % 2), ground - 4 - k - i, WISP, max(20, int(150 * fade) - k * 25))
    # terapkan alpha keseluruhan utk frame memudar
    if alpha < 255:
        for y in range(art.h):
            for x in range(art.w):
                c = art.px[y][x]
                if c[3] > 0:
                    art.px[y][x] = (c[0], c[1], c[2], alpha)
    return art, glow


# --------------------------------------------------------------------------- #
# tabel pose: 23 frame sesuai PROMPTS.md section 2
# --------------------------------------------------------------------------- #
def P(hip=(0, 0), lean=0, head=0, legF=(4, -6), legB=(-5, 6), armF=(-3, -4), armB=(4, 5), sword=0, glow=0, lift=0):
    """lift = berapa piksel tubuh diangkat dari posisi menapak (frame di udara)."""
    return dict(hip=hip, lean=lean, head=head, legF=legF, legB=legB, armF=armF, armB=armB,
                sword=sword, glow=glow, lift=lift)


POSES: dict[str, dict] = {
    # IDLE — berdiri, bernapas
    "idle_1": P(hip=(0, 0), lean=0, head=0, legF=(4, -6), legB=(-5, 6), armF=(-3, -4), armB=(4, 5), sword=0),
    "idle_2": P(hip=(0, 1), lean=2, head=6, legF=(3, -4), legB=(-4, 5), armF=(-2, -3), armB=(3, 4), sword=0),
    # WALK — 4 frame: contact / passing / contact mirror / passing mirror
    "walk_1": P(hip=(0, 0), lean=2, head=1, legF=(22, -10), legB=(-20, 14), armF=(-16, -8), armB=(16, 8), sword=-8),
    "walk_2": P(hip=(0, -1), lean=2, head=0, legF=(3, -5), legB=(-3, 20), armF=(-6, -4), armB=(6, 4), sword=-4),
    "walk_3": P(hip=(0, 0), lean=2, head=1, legF=(-20, 14), legB=(22, -10), armF=(16, 8), armB=(-16, -8), sword=2),
    "walk_4": P(hip=(0, 1), lean=2, head=0, legF=(-3, 20), legB=(3, -5), armF=(6, 4), armB=(-6, -4), sword=0),
    # RUN — condong ke depan, ekspresif
    "run_1": P(hip=(1, -1), lean=14, head=-4, legF=(34, -16), legB=(-32, 22), armF=(-40, -50), armB=(45, 45), sword=-140),
    "run_2": P(lean=14, head=-2, legF=(10, -50), legB=(-10, 50), armF=(-20, -30), armB=(25, 35), sword=-130, lift=2),
    "run_3": P(hip=(1, -1), lean=14, head=-4, legF=(-32, 22), legB=(34, -16), armF=(45, 45), armB=(-40, -50), sword=-140),
    "run_4": P(lean=14, head=-2, legF=(-10, 50), legB=(10, -50), armF=(20, 30), armB=(-25, -30), sword=-130, lift=1),
    # JUMP — crouch / rising / falling
    "jump_1": P(lean=8, head=6, legF=(38, -60), legB=(-30, 55), armF=(-50, -25), armB=(45, 20), sword=-150),
    "jump_2": P(lean=2, head=-6, legF=(42, -70), legB=(25, -75), armF=(135, 145), armB=(150, 140), sword=35, lift=6),
    "jump_3": P(lean=8, head=3, legF=(18, -8), legB=(-14, 6), armF=(70, 20), armB=(-70, -20), sword=25, lift=7),
    # ATTACK — windup / strike / recovery
    "attack_1": P(hip=(-1, 0), lean=-8, head=-8, legF=(14, -8), legB=(-14, 12), armF=(150, 160), armB=(120, 40), sword=20),
    "attack_2": P(hip=(1, 1), lean=18, head=8, legF=(42, -22), legB=(-38, 26), armF=(85, 25), armB=(30, -20), sword=115),
    "attack_3": P(hip=(0, 1), lean=6, head=4, legF=(10, -6), legB=(-10, 8), armF=(20, 10), armB=(10, 5), sword=15),
    # SKILL — charging / release / aftercast
    "skill_1": P(lean=0, head=2, legF=(16, -26), legB=(-14, 22), armF=(35, 25), armB=(42, 30), sword=0, glow=0.4),
    "skill_2": P(hip=(0, -1), lean=-6, head=-4, legF=(12, -10), legB=(-12, 14), armF=(150, 162), armB=(140, 150), sword=8, glow=1.0),
    "skill_3": P(hip=(0, 2), lean=8, head=8, legF=(10, -16), legB=(-8, 14), armF=(15, 10), armB=(8, 6), sword=0, glow=0.15),
    # HURT
    "hurt_1": P(hip=(-2, 1), lean=-25, head=-14, legF=(-24, 12), legB=(26, -6), armF=(-70, -25), armB=(80, 30), sword=-60),
    # DEATH — collapse / fallen / faded
    "death_1": P(lean=40, head=20, legF=(30, -50), legB=(-20, 40), armF=(-40, 30), armB=(50, 20), sword=70),
}
PRONE_FRAMES = {"death_2": 0.0, "death_3": 0.7}


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    frames_dir = root / "assets/character/frames"
    gen_dir = root / "assets/character/generated"
    ref_dir = root / "assets/character/reference"
    for d in (frames_dir, gen_dir, ref_dir):
        d.mkdir(parents=True, exist_ok=True)

    order = list(POSES) + list(PRONE_FRAMES)
    for name in order:
        art, glow = render_prone(PRONE_FRAMES[name]) if name in PRONE_FRAMES else render_pose(POSES[name])
        final = with_outline(art, glow)
        final.to_image().save(frames_dir / f"{name}.png")

    # titik kaki (output px) -> dipakai engine supaya sprite menapak tepat di tanah.
    # Nilainya dibaca dari hasil render idle_1 supaya selalu cocok dengan gambar asli.
    with Image.open(frames_dir / "idle_1.png") as idle:
        box = idle.getchannel("A").getbbox()
    if box is None:
        raise SystemExit("idle_1.png kosong - render gagal")
    anchor = {
        "character": "arga",
        "unit": UNIT,
        "cell": {"w": LOGICAL_W * UNIT, "h": LOGICAL_H * UNIT},
        # titik telapak kaki di dalam kanvas (px output) = dasar tempat sprite dipasang
        "foot": {"x": int(round(HIP[0])) * UNIT, "y": GROUND_ROW * UNIT + UNIT - 1},
        "stand_height": (GROUND_ROW - 8 + 1) * UNIT,
        "idle_bbox": list(box),
        "frames": len(order),
        "order": order,
    }
    (gen_dir / "anchor.json").write_text(json.dumps(anchor, indent=2) + "\n", encoding="utf-8")

    # reference image 4x (background magenta, sesuai spesifikasi PROMPTS.md)
    art, glow = render_pose(POSES["idle_1"])
    ref = with_outline(art, glow).to_image(4)
    canvas = Image.new("RGBA", ref.size, MAGENTA)
    canvas.alpha_composite(ref)
    canvas.save(ref_dir / "hero-reference.png")

    print(f"{len(order)} frame -> {frames_dir}")
    print(f"anchor -> {gen_dir / 'anchor.json'}")
    print(f"reference 4x -> {ref_dir / 'hero-reference.png'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
