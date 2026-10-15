#!/usr/bin/env python3
"""Rakit frame PNG karakter menjadi spritesheet siap pakai di Godot 4.

Alur kerja:
  1. Baca semua frame dari `assets/character/frames/<nama>.png` sesuai urutan
     animasi di `assets/character/character.json`.
  2. Buang background chroma key magenta (#FF00FF) -> alpha, plus despill fringe.
  3. Skalakan SEMUA frame dengan faktor yang sama (diambil dari frame referensi
     `scale_ref`, default idle_1) supaya tinggi badan konsisten antar frame
     -> animasi tidak jitter / tidak "goyang" saat diputar.
  4. Tempel tiap frame ke dalam cell berukuran seragam, rata bawah (kaki tetap
     di tanah).
  5. Tulis 3 artefak:
       - <nama>_spritesheet.png   : PNG RGBA
       - <nama>_spritesheet.json  : metadata atlas (rect cell + rect konten)
       - <nama>_frames.tres       : resource SpriteFrames Godot 4

Contoh:
    python3 tools/make_spritesheet.py --target-height 64
    python3 tools/make_spritesheet.py --only idle,walk --cell 32x32
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    from PIL import Image, ImageChops
except ImportError:  # pragma: no cover - hanya pesan bantuan
    sys.exit(
        "Pillow belum terpasang. Jalankan:\n"
        "    python3 -m pip install -r requirements.txt"
    )

DEFAULT_CONFIG = "assets/character/character.json"
# "canvas" = jangan trim: pakai koordinat kanvas asli frame (wajib kalau semua
# frame punya kanvas sama, mis. hasil tools/generate_character_frames.py, supaya
# tinggi lompatan / pose rebah tetap benar).
ALIGN_CHOICES = ("canvas", "bottom-center", "bottom-left", "center")
RESAMPLE_CHOICES = ("auto", "nearest", "box", "lanczos")


# --------------------------------------------------------------------------- #
# util
# --------------------------------------------------------------------------- #
def log(msg: str, quiet: bool = False) -> None:
    if not quiet:
        print(msg)


def warn(msg: str) -> None:
    print(f"  ! {msg}", file=sys.stderr)


def parse_hex(value: str) -> tuple[int, int, int]:
    text = value.strip().lstrip("#")
    if len(text) == 8:  # dukung #RRGGBBAA -> ambil RGB saja
        text = text[:6]
    if len(text) != 6:
        raise ValueError(f"warna hex tidak valid: {value!r}")
    return tuple(int(text[i : i + 2], 16) for i in (0, 2, 4))  # type: ignore[return-value]


def parse_size(value: str) -> tuple[int, int]:
    sep = "x" if "x" in value.lower() else ","
    parts = value.lower().split(sep)
    if len(parts) != 2:
        raise argparse.ArgumentTypeError("format ukuran harus WxH, contoh 32x48")
    w, h = (int(p) for p in parts)
    if w <= 0 or h <= 0:
        raise argparse.ArgumentTypeError("ukuran cell harus > 0")
    return w, h


def hex_str(rgb: tuple[int, int, int]) -> str:
    return "#{:02x}{:02x}{:02x}".format(*rgb)


def res_path(target: Path, project_root: Path) -> str:
    """Path Godot `res://...` untuk file di dalam project root."""
    target = target.resolve()
    root = project_root.resolve()
    try:
        rel = target.relative_to(root)
    except ValueError:
        warn(
            f"{target} berada di luar project root {root}; "
            f"ext_resource memakai nama file saja ({target.name})"
        )
        return f"res://{target.name}"
    return "res://" + rel.as_posix()


# --------------------------------------------------------------------------- #
# chroma key
# --------------------------------------------------------------------------- #
def chroma_key(
    img: Image.Image,
    key: tuple[int, int, int],
    tolerance: int,
    despill: bool,
    spill: int,
) -> Image.Image:
    """Jadikan warna key -> transparan, lalu kurangi magenta spill di tepi.

    Alpha dihitung dari jarak Chebyshev piksel ke warna key (operasi C-level
    Pillow, jadi tetap cepat walau gambarnya besar).
    """
    rgba = img.convert("RGBA")
    rgb = rgba.convert("RGB")
    key_layer = Image.new("RGB", rgba.size, key)
    diff = ImageChops.difference(rgb, key_layer).split()
    dist = ImageChops.lighter(ImageChops.lighter(diff[0], diff[1]), diff[2])

    # mask 255 = piksel background -> dibuang
    key_mask = dist.point(lambda v: 255 if v <= tolerance else 0)

    if despill:
        r, g, b = rgb.split()
        lifted = g.point(lambda v: min(255, v + spill))
        r2 = ImageChops.darker(r, lifted)
        b2 = ImageChops.darker(b, lifted)
        cleaned = Image.merge("RGB", (r2, g, b2))
        spill_mask = dist.point(lambda v: 255 if v <= tolerance * 3 else 0)
        rgb = Image.composite(cleaned, rgb, spill_mask)

    alpha = ImageChops.multiply(rgba.getchannel("A"), ImageChops.invert(key_mask))
    red, green, blue = rgb.split()
    return Image.merge("RGBA", (red, green, blue, alpha))


def content_box(img: Image.Image) -> tuple[int, int, int, int] | None:
    return img.getchannel("A").getbbox()


# --------------------------------------------------------------------------- #
# data
# --------------------------------------------------------------------------- #
@dataclass
class FrameArt:
    name: str
    animation: str
    image: Image.Image  # RGBA, sudah di-key (di-crop ke konten kecuali mode canvas)
    source_content: tuple[int, int]  # ukuran konten (bbox alpha) SEBELUM skala
    source_frame: tuple[int, int]  # ukuran kanvas PNG asli SEBELUM skala
    source_box: tuple[int, int, int, int]  # bbox alpha di kanvas asli
    crop_origin: tuple[int, int]  # sudut kiri-atas image terhadap kanvas asli


@dataclass
class Placed:
    frame: FrameArt
    col: int
    row: int
    rect: tuple[int, int, int, int]  # rect cell di sheet
    content_rect: tuple[int, int, int, int]  # rect konten di sheet
    offset: tuple[int, int]  # posisi konten di dalam cell


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #
def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description="Rakit frame karakter menjadi spritesheet + atlas (Godot 4).",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    p.add_argument("--config", default=DEFAULT_CONFIG, help="file config karakter")
    p.add_argument("--frames-dir", help="folder frame PNG (override config)")
    p.add_argument("--out", help="path spritesheet PNG (override config)")
    p.add_argument("--atlas", help="path metadata JSON (override config)")
    p.add_argument("--tres", help="path resource SpriteFrames Godot (override config)")
    p.add_argument(
        "--target-height",
        type=int,
        default=None,
        help="tinggi badan (px) pada frame scale_ref; 0 = tanpa resize",
    )
    p.add_argument("--cell", type=parse_size, help="paksa ukuran cell, mis. 64x64")
    p.add_argument("--columns", type=int, help="jumlah kolom sheet (default: grid persegi)")
    p.add_argument("--padding", type=int, help="margin transparan di dalam cell")
    p.add_argument("--align", choices=ALIGN_CHOICES, help="penempatan konten di cell")
    p.add_argument("--resample", choices=RESAMPLE_CHOICES, help="filter resize")
    p.add_argument("--tolerance", type=int, help="toleransi chroma key (0-255)")
    p.add_argument("--spill", type=int, help="kekuatan despill fringe magenta")
    p.add_argument("--no-chroma-key", action="store_true", help="lewati penghapusan background")
    p.add_argument("--no-despill", action="store_true", help="lewati despill fringe")
    p.add_argument("--only", help="batasi animasi, mis. idle,walk")
    p.add_argument("--strict", action="store_true", help="gagal kalau ada frame hilang")
    p.add_argument("--quiet", action="store_true", help="hanya cetak error")
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)

    cfg_path = Path(args.config)
    if not cfg_path.is_file():
        print(f"Config tidak ditemukan: {cfg_path}", file=sys.stderr)
        return 1
    cfg = json.loads(cfg_path.read_text(encoding="utf-8"))

    project_root = Path(cfg.get("project_root", "."))
    character = cfg.get("name", "character")
    frames_dir = Path(args.frames_dir or cfg.get("frames_dir", "assets/character/frames"))
    out_cfg = cfg.get("output", {})
    sheet_path = Path(args.out or out_cfg.get("sheet") or f"assets/character/build/{character}_spritesheet.png")
    atlas_path = Path(args.atlas or out_cfg.get("atlas") or f"assets/character/build/{character}_spritesheet.json")
    tres_path = Path(args.tres or out_cfg.get("godot") or f"assets/character/build/{character}_frames.tres")

    target_height = args.target_height if args.target_height is not None else int(cfg.get("target_height") or 0)
    tolerance = args.tolerance if args.tolerance is not None else int(cfg.get("tolerance", 48))
    spill = args.spill if args.spill is not None else int(cfg.get("spill", 60))
    padding = args.padding if args.padding is not None else int(cfg.get("padding", 1))
    align = args.align or str(cfg.get("align", "bottom-center"))
    resample = args.resample or str(cfg.get("resample", "auto"))
    key_color = parse_hex(cfg.get("chroma_key", "#FF00FF"))
    scale_ref = str(cfg.get("scale_ref", ""))
    canvas_mode = align == "canvas"
    do_chroma = not args.no_chroma_key
    do_despill = do_chroma and not args.no_despill

    animations: list[dict] = cfg.get("animations", [])
    if not animations:
        print("Config tidak punya daftar 'animations'.", file=sys.stderr)
        return 1

    only = {s.strip() for s in args.only.split(",") if s.strip()} if args.only else None
    config_names = {name for anim in animations for name in anim.get("frames", [])}
    if only:
        unknown = sorted(only - {anim["name"] for anim in animations})
        if unknown:
            warn(f"--only menyebut animasi yang tidak ada di config: {', '.join(unknown)}")

    # ------------------------------------------------------------------ load
    log(f"Karakter   : {character}", args.quiet)
    log(f"Frame dir  : {frames_dir}", args.quiet)
    missing: list[tuple[str, str]] = []
    arts: list[FrameArt] = []
    ordered: list[tuple[str, str]] = []  # (animation, frame name) sesuai config

    for anim in animations:
        anim_name = anim["name"]
        if only and anim_name not in only:
            continue
        for frame_name in anim.get("frames", []):
            path = frames_dir / f"{frame_name}.png"
            if not path.is_file():
                missing.append((anim_name, frame_name))
                continue
            with Image.open(path) as raw:
                img = chroma_key(raw, key_color, tolerance, do_despill, spill) if do_chroma else raw.convert("RGBA")
            box = content_box(img)
            if box is None:
                warn(f"{path.name}: seluruh gambar transparan, frame dilewati")
                continue
            arts.append(
                FrameArt(
                    name=frame_name,
                    animation=anim_name,
                    image=img if canvas_mode else img.crop(box),
                    source_content=(box[2] - box[0], box[3] - box[1]),
                    source_frame=img.size,
                    source_box=tuple(box),
                    crop_origin=(0, 0) if canvas_mode else (box[0], box[1]),
                )
            )
            ordered.append((anim_name, frame_name))

    if missing:
        names = ", ".join(f"{anim}/{name}" for anim, name in missing)
        warn(f"{len(missing)} frame belum ada: {names}")
        log("      (frame yang tidak ada dilewati; generate dari PROMPTS.md lalu jalankan ulang)", args.quiet)

    if not arts:
        print(
            f"Tidak ada frame PNG di {frames_dir}.\n"
            f"Taruh file seperti idle_1.png, walk_1.png, ... (lihat bagian 3 PROMPTS.md).",
            file=sys.stderr,
        )
        return 1
    if missing and args.strict:
        print(f"--strict aktif dan {len(missing)} frame masih kurang.", file=sys.stderr)
        return 1

    if canvas_mode:
        sizes = {a.source_frame for a in arts}
        if len(sizes) > 1:
            warn(
                f"align=canvas butuh kanvas yang sama untuk semua frame, ditemukan {len(sizes)} ukuran berbeda "
                f"({sorted(sizes)}) -> posisi tiap frame bisa tidak konsisten"
            )

    # peringatkan PNG yang tidak terdaftar di config
    if frames_dir.is_dir():
        for stray in sorted(frames_dir.glob("*.png")):
            if stray.stem not in config_names:
                warn(f"{stray.name} tidak terdaftar di character.json -> diabaikan")

    # ---------------------------------------------------------------- scaling
    if target_height > 0:
        ref = next((a for a in arts if a.name == scale_ref), None) or max(arts, key=lambda a: a.image.height)
        if ref.name != scale_ref and scale_ref:
            warn(f"frame referensi '{scale_ref}' tidak ada, memakai '{ref.name}' sebagai acuan skala")
        if ref.source_content[1] <= 0:
            print(f"frame acuan '{ref.name}' kosong.", file=sys.stderr)
            return 1
        scale = target_height / ref.source_content[1]  # tinggi KONTEN frame acuan (bukan tinggi kanvas)
        if scale > 1:
            log(
                f"Skala      : {scale:.3f}x dari {ref.name} (konten {ref.source_content[1]}px -> {target_height}px)",
                args.quiet,
            )
        elif scale < 1:
            warn(
                f"mengecilkan konten {ref.name} {ref.source_content[1]}px -> {target_height}px ({scale:.3f}x); "
                "hasil terbaik kalau frame digenerate mendekati ukuran akhir"
            )
        if resample == "auto":
            resample = "nearest" if scale >= 1 else "box"
    else:
        scale = 1.0
        if resample == "auto":
            resample = "nearest"
    resample_id = {
        "nearest": Image.Resampling.NEAREST,
        "box": Image.Resampling.BOX,
        "lanczos": Image.Resampling.LANCZOS,
    }[resample]

    scaled: list[FrameArt] = []
    for art in arts:
        w = max(1, round(art.image.width * scale))
        h = max(1, round(art.image.height * scale))
        img = art.image.resize((w, h), resample_id) if (w, h) != art.image.size else art.image
        scaled.append(
            FrameArt(
                art.name,
                art.animation,
                img,
                art.source_content,
                art.source_frame,
                art.source_box,
                art.crop_origin,
            )
        )

    if args.cell:
        cell_w, cell_h = args.cell
        max_w = max(a.image.width for a in scaled)
        max_h = max(a.image.height for a in scaled)
        if max_w + 2 * padding > cell_w or max_h + 2 * padding > cell_h:
            warn(
                f"frame terbesar {max_w}x{max_h}px + padding tidak muat di cell {cell_w}x{cell_h} "
                "-> konten dipotong; naikkan --cell kalau tidak diinginkan"
            )
    else:
        cell_w = max(a.image.width for a in scaled) + 2 * padding
        cell_h = max(a.image.height for a in scaled) + 2 * padding

    # ------------------------------------------------------------------- pack
    columns = args.columns or max(1, math.ceil(math.sqrt(len(scaled))))
    columns = max(1, min(columns, len(scaled)))
    rows = math.ceil(len(scaled) / columns)
    sheet_w, sheet_h = columns * cell_w, rows * cell_h
    sheet = Image.new("RGBA", (sheet_w, sheet_h), (0, 0, 0, 0))

    placed: list[Placed] = []
    for index, art in enumerate(scaled):
        col, row = index % columns, index // columns
        w, h = art.image.size
        if canvas_mode:
            # pertahankan koordinat kanvas asli: samping kiri-atas
            off_x, off_y = padding, padding
        elif align == "bottom-center":
            off_x, off_y = (cell_w - w) // 2, cell_h - h - padding
        elif align == "bottom-left":
            off_x, off_y = padding, cell_h - h - padding
        else:
            off_x, off_y = (cell_w - w) // 2, (cell_h - h) // 2
        off_x = max(0, off_x)
        off_y = max(0, off_y)
        # cell terlalu kecil (mis. dari --cell) -> potong, jangan sampai melimpah
        # ke cell tetangga dan merusak frame lain.
        clip_w, clip_h = min(w, cell_w - off_x), min(h, cell_h - off_y)
        content = art.image.crop((0, 0, clip_w, clip_h)) if (clip_w, clip_h) != (w, h) else art.image
        x, y = col * cell_w, row * cell_h
        sheet.alpha_composite(content, (x + off_x, y + off_y))

        # rect konten = bbox alpha yang benar-benar tergambar di sheet (bukan cell),
        # supaya metadata tetap jujur untuk mode `canvas` maupun mode crop.
        box_x, box_y, box_r, box_b = art.source_box
        content_x = x + off_x + round((box_x - art.crop_origin[0]) * scale)
        content_y = y + off_y + round((box_y - art.crop_origin[1]) * scale)
        content_w = max(1, round((box_r - box_x) * scale))
        content_h = max(1, round((box_b - box_y) * scale))
        # potong ke batas cell kalau konten memang terpotong
        content_w = max(1, min(content_w, x + cell_w - content_x))
        content_h = max(1, min(content_h, y + cell_h - content_y))
        content_x = min(content_x, x + cell_w - 1)
        content_y = min(content_y, y + cell_h - 1)
        placed.append(
            Placed(
                frame=art,
                col=col,
                row=row,
                rect=(x, y, cell_w, cell_h),
                content_rect=(content_x, content_y, content_w, content_h),
                offset=(off_x, off_y),
            )
        )

    # ------------------------------------------------------------------ write
    sheet_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(sheet_path)

    animations_out = []
    for anim in animations:
        if only and anim["name"] not in only:
            continue
        frames = [a.name for a in arts if a.animation == anim["name"]]
        if not frames:
            continue
        animations_out.append(
            {"name": anim["name"], "fps": anim.get("fps", 8), "loop": bool(anim.get("loop", False)), "frames": frames}
        )

    atlas = {
        "generator": "tools/make_spritesheet.py",
        "character": character,
        "display_name": cfg.get("display_name", character),
        "image": sheet_path.name,
        "sheet_size": {"w": sheet_w, "h": sheet_h},
        "cell": {"w": cell_w, "h": cell_h, "padding": padding, "columns": columns, "rows": rows},
        "pivot": align,
        "scale": round(scale, 6),
        "target_height": target_height,
        "chroma_key": hex_str(key_color),
        "source_content": {a.name: {"w": a.source_content[0], "h": a.source_content[1]} for a in arts},
        "source_frame": {a.name: {"w": a.source_frame[0], "h": a.source_frame[1]} for a in arts},
        "frames": [
            {
                "name": p.frame.name,
                "animation": p.frame.animation,
                "rect": {"x": p.rect[0], "y": p.rect[1], "w": p.rect[2], "h": p.rect[3]},
                "content": {"x": p.content_rect[0], "y": p.content_rect[1], "w": p.content_rect[2], "h": p.content_rect[3]},
                "cell": {"col": p.col, "row": p.row},
                "size": {"w": p.frame.image.width, "h": p.frame.image.height},
                "offset": {"x": p.offset[0], "y": p.offset[1]},
            }
            for p in placed
        ],
        "animations": animations_out,
    }
    atlas_path.parent.mkdir(parents=True, exist_ok=True)
    atlas_path.write_text(json.dumps(atlas, indent=2) + "\n", encoding="utf-8")

    tres_path.parent.mkdir(parents=True, exist_ok=True)
    tres_path.write_text(
        build_godot_tres(character, res_path(sheet_path, project_root), placed, animations_out),
        encoding="utf-8",
    )

    log(
        f"Sheet      : {sheet_path}  ({sheet_w}x{sheet_h}px, cell {cell_w}x{cell_h}, {columns}x{rows})",
        args.quiet,
    )
    log(f"Atlas JSON : {atlas_path}", args.quiet)
    log(f"Godot .tres: {tres_path}", args.quiet)
    total = sum(len(a["frames"]) for a in animations_out)
    log(f"Selesai    : {total} frame di {len(animations_out)} animasi", args.quiet)
    return 0


# --------------------------------------------------------------------------- #
# Godot 4 SpriteFrames
# --------------------------------------------------------------------------- #
def build_godot_tres(character: str, texture_res: str, placed: list[Placed], animations: list[dict]) -> str:
    """Tulis resource SpriteFrames (Godot 4) dengan region = rect CELL.

    Region memakai rect cell penuh (bukan konten) supaya semua frame punya
    ukuran identik -> AnimatedSprite2D tidak jitter walau kontennya berbeda.
    """
    by_name: dict[str, Placed] = {p.frame.name: p for p in placed}
    ids: dict[str, str] = {p.frame.name: f"AtlasTexture_{character}_{p.frame.name}" for p in placed}
    used: list[Placed] = []
    seen: set[str] = set()
    for anim in animations:
        for frame_name in anim["frames"]:
            if frame_name in by_name and frame_name not in seen:
                seen.add(frame_name)
                used.append(by_name[frame_name])

    lines = [f'[gd_resource type="SpriteFrames" load_steps={1 + 1 + len(used)} format=3]', ""]
    lines.append(f'[ext_resource type="Texture2D" path="{texture_res}" id="1_sheet"]')
    lines.append("")
    for p in used:
        x, y, w, h = p.rect
        lines.append(f'[sub_resource type="AtlasTexture" id="{ids[p.frame.name]}"]')
        lines.append('atlas = ExtResource("1_sheet")')
        lines.append(f"region = Rect2({x}, {y}, {w}, {h})")
        lines.append("")
    lines.append("[resource]")
    lines.append("animations = [{")
    for index, anim in enumerate(animations):
        lines.append('"frames": [{')
        for frame_index, frame_name in enumerate(anim["frames"]):
            sep = "," if frame_index < len(anim["frames"]) - 1 else ""
            lines.append('"duration": 1.0,')
            lines.append(f'"texture": SubResource("{ids[frame_name]}")')
            lines.append("}" + sep)
        lines.append("],")
        lines.append(f'"loop": {"true" if anim["loop"] else "false"},')
        lines.append(f'"name": &"{anim["name"]}",')
        lines.append(f'"speed": {float(anim["fps"])}')
        lines.append("}" + ("," if index < len(animations) - 1 else ""))
    lines.append("}]")
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    raise SystemExit(main())
