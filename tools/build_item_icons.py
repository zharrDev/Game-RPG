#!/usr/bin/env python3
"""Ikon item pixel art 16x16 (alpha biner) untuk HUD F1.

Masker kain: kain berlapis arang dengan tali di kedua sisi. Palet kecil,
tanpa anti-aliasing, supaya konsisten dengan atlas lain di game.
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "game" / "assets" / "ui"
PREVIEW = ROOT / "docs" / "preview-items.png"

TRANSPARENT = (0, 0, 0, 0)
OUTLINE = (40, 32, 28, 255)
CLOTH = (198, 180, 142, 255)
CLOTH_LIGHT = (216, 200, 164, 255)
CLOTH_SHADE = (158, 138, 104, 255)
CHARCOAL = (58, 56, 52, 255)
CHARCOAL_LIGHT = (88, 84, 76, 255)
STRAP = (94, 66, 42, 255)

# Baris per baris: 16 kolom, karakter -> warna.
MASK = [
    "................",
    "................",
    "..S..........S..",
    "..S.OOOOOOOO.S..",
    "..S.OCLLLLCO.S..",
    "..S.OCLLLLCO.S..",
    ".SS.OLCCCCLO.SS.",
    ".SS.OLCCCCLO.SS.",
    ".SS.OLCCCCLO.SS.",
    ".SS.OLCCCCLO.SS.",
    "..S.OCLLLLCO.S..",
    "..S.OCLLLLCO.S..",
    "..S.OOOOOOOO.S..",
    "..S..........S..",
    "................",
    "................",
]

PALETTE = {
    ".": TRANSPARENT,
    "O": OUTLINE,
    "C": CLOTH,
    "L": CLOTH_LIGHT,
    "S": STRAP,
}
# Baris charcoal filter ditambahkan lewat overlay supaya kode tetap ringkas.
FILTER = {(col, row) for row in range(6, 10) for col in range(5, 11)}


def build_masker() -> Image.Image:
    image = Image.new("RGBA", (16, 16), TRANSPARENT)
    pixels = image.load()
    for row, line in enumerate(MASK):
        assert len(line) == 16, f"baris {row} bukan 16 kolom"
        for col, char in enumerate(line):
            color = PALETTE[char]
            if (col, row) in FILTER and char in "CL":
                color = CHARCOAL if (row + col) % 2 == 0 else CHARCOAL_LIGHT
            pixels[col, row] = color
    # jahitan tepi filter atas/bawah
    for col in range(5, 11):
        pixels[col, 5] = CLOTH_SHADE
        pixels[col, 10] = CLOTH_SHADE
    return image


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    icon = build_masker()
    icon.save(OUT / "icon_masker.png")
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    strip = Image.new("RGBA", (48, 24), (24, 22, 26, 255))
    strip.paste(icon, (4, 4), icon)
    strip = strip.resize((192, 96), Image.Resampling.NEAREST)
    strip.save(PREVIEW)
    colors = {pixel for pixel in icon.getdata() if pixel[3] > 0}
    print(f"[ikon] {OUT / 'icon_masker.png'} 16x16, {len(colors)} warna opaque")
    print(f"[pratinjau] {PREVIEW}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
