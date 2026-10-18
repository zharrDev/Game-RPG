#!/usr/bin/env python3
"""Verifikasi artefak aset hasil tools/generate_*.py & tools/build_*.py.

Dijalankan di scripts/test-all.sh. Memeriksa hal-hal yang tidak bisa dilihat
sekilas: tile saling menimpa, tile kosong, nama tile yang hilang, grid walkable
yang tidak konsisten, frame sprite di luar atlas, dan animasi yang kosong.

Jalankan:
    python3 tools/verify_assets.py
"""

from __future__ import annotations

import fnmatch
import json
import re
import struct
import sys
import wave
from collections import defaultdict
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
TILES = ROOT / "game/assets/tiles"
WORLD = ROOT / "game/assets/world"
CHAR = ROOT / "game/assets/character"
FONT = ROOT / "game/assets/font"
AUDIO = ROOT / "game/assets/audio"

FAILURES: list[str] = []
CHECKS = 0


def check(name: str, ok: bool, detail: str = "") -> None:
    global CHECKS
    CHECKS += 1
    if ok:
        print(f"ok    {name}")
    else:
        FAILURES.append(f"{name} {detail}".strip())
        print(f"FAIL  {name}  {detail}")


def cell_opaque(atlas: Image.Image, rect, solid_required: bool) -> tuple[int, int]:
    im = atlas.crop((rect[0], rect[1], rect[0] + rect[2], rect[1] + rect[3])).convert("RGBA")
    px = list(im.getdata())
    opaque = sum(1 for p in px if p[3] > 8)
    return opaque, len(px)


def verify_tileset() -> dict:
    print("== tileset ==")
    meta = json.loads((TILES / "tileset.json").read_text())
    atlas = Image.open(TILES / "tileset.png").convert("RGBA")
    check("atlas RGBA & ukuran = kolom x baris x tile",
          atlas.size == (meta["columns"] * meta["tile_size"], meta["rows"] * meta["tile_size"]),
          f"{atlas.size}")

    occupied: dict[tuple[int, int], str] = {}
    overlap: list[str] = []
    overflow: list[str] = []
    for name, t in meta["tiles"].items():
        for dy in range(t["h"]):
            for dx in range(t["w"]):
                cell = (t["col"] + dx, t["row"] + dy)
                if cell in occupied:
                    overlap.append(f"{name} vs {occupied[cell]} di {cell}")
                occupied[cell] = name
                if cell[0] >= meta["columns"] or cell[1] >= meta["rows"]:
                    overflow.append(f"{name}@{cell}")
    check("tidak ada tile yang saling menimpa", not overlap, "; ".join(overlap[:3]))
    check("tidak ada tile keluar dari atlas", not overflow, "; ".join(overflow[:3]))

    empty = []
    transparent = []
    for name, t in meta["tiles"].items():
        opaque, total = cell_opaque(atlas, [t["x"], t["y"], t["w"] * 16, t["h"] * 16], True)
        if opaque == 0:
            empty.append(name)
        if opaque < total * 0.05:
            transparent.append(f"{name} {opaque / total:.0%}")
    check("tidak ada tile kosong", not empty, "; ".join(empty[:5]))
    check("tidak ada tile nyaris transparan", not transparent, "; ".join(transparent[:5]))

    # cell vs object konsisten
    cells_by_group = defaultdict(list)
    for cell in meta["cells"]:
        cells_by_group[cell["group"]].append(cell)
    mismatch = []
    for name, t in meta["tiles"].items():
        cells = cells_by_group.get(name, [])
        if len(cells) != t["w"] * t["h"]:
            mismatch.append(f"{name}: {len(cells)} cell != {t['w'] * t['h']}")
        solid_expected = {(dx, dy) for dx, dy in t["solid_cells"]} if t["solid_cells"] else (
            {(dx, dy) for dy in range(t["h"]) for dx in range(t["w"])} if t["solid"] else set())
        solid_actual = {(c["dx"], c["dy"]) for c in cells if c["solid"]}
        if solid_expected != solid_actual:
            mismatch.append(f"{name}: solid cell beda")
    check("jumlah & collision cell cocok dengan objek", not mismatch, "; ".join(mismatch[:3]))

    # kualitas gaya: hanya warna palet, tanpa anti-aliasing (alpha biner) & tanpa magenta
    px = list(atlas.getdata())
    alpha_values = {p[3] for p in px}
    magenta = sum(1 for p in px if p[3] > 8 and p[0] > 200 and p[1] < 60 and p[2] > 200)
    check("alpha biner (tanpa anti-aliasing)", alpha_values <= {0, 255}, f"{sorted(alpha_values)[:6]}")
    check("tidak ada sisa background magenta", magenta == 0, f"{magenta} px")
    check("jumlah warna terbatas (palet kecil)", len(set(px)) <= 96, f"{len(set(px))} warna")
    return meta


def verify_world(meta: dict) -> None:
    print("== peta overworld ==")
    data = json.loads((WORLD / "overworld.json").read_text())
    w, h = data["width"], data["height"]
    check("ground_tiles lengkap", len(data["ground_tiles"]) == h and all(len(r) == w for r in data["ground_tiles"]))
    check("walkable lengkap", len(data["walkable"]) == h and all(len(r) == w for r in data["walkable"]))
    check("tile_names terisi", len(data["tile_names"]) > 0)

    missing = sorted({n for n in data["tile_names"] if n not in meta["tiles"]})
    check("semua tile ground ada di tileset", not missing, "; ".join(missing[:5]))

    # animasi air: tiap tile pantai punya pasangan fase ke-2; peta memakai fase 0
    shore_tiles = [n for n in meta["tiles"] if n.startswith("water_") and not n.endswith("_b")
                   and not n.startswith("water_deep")]
    paired = [n for n in shore_tiles if f"{n}_b" in meta["tiles"]]
    check("setiap tile pantai punya pasangan fase animasi", len(paired) == 16, f"{len(paired)}/{len(shore_tiles)}")
    phase_b_used = [n for n in data["tile_names"] if n.startswith("water_") and n.endswith("_b")]
    check("peta memakai fase air 0 (game yang menukar fase)",
          not phase_b_used, "; ".join(phase_b_used[:3]))
    deep = [n for n in meta["tiles"] if n.startswith("water_deep")]
    check("air dalam punya 3 varian riak", len(deep) == 3, str(len(deep)))

    bad_props = sorted({p["group"] for p in data["props"] if p["group"] not in meta["tiles"]})
    check("semua prop ada di tileset", not bad_props, "; ".join(bad_props[:5]))

    # walkable harus = terrain yang bisa dilalui dikurangi tapak prop solid
    walk_terrain = {"grass", "meadow", "dirt", "cobble", "sand", "stone", "cave", "bridge"}
    solid_cells = defaultdict(set)
    for cell in meta["cells"]:
        if cell["solid"]:
            solid_cells[cell["group"]].add((cell["dx"], cell["dy"]))
    expected = [[data["terrain_names"][data["terrain"][y][x]] in walk_terrain for x in range(w)] for y in range(h)]
    for prop in data["props"]:
        for dx, dy in solid_cells.get(prop["group"], set()):
            cx, cy = prop["col"] + dx, prop["row"] + dy
            if 0 <= cx < w and 0 <= cy < h:
                expected[cy][cx] = False
    mismatch = sum(1 for y in range(h) for x in range(w) if expected[y][x] != bool(data["walkable"][y][x]))
    check("grid walkable konsisten dengan terrain + prop", mismatch == 0, f"{mismatch} sel beda")

    spawn = data["markers"]["spawn"]
    check("spawn berdiri di tile walkable", bool(data["walkable"][spawn["row"]][spawn["col"]]))
    bad_cp = [c["id"] for c in data["markers"]["checkpoints"] if not data["walkable"][c["row"]][c["col"]]]
    check("semua checkpoint bisa dipijak", not bad_cp, "; ".join(bad_cp))
    bad_mob = [f"{e['type']}@{e['col']},{e['row']}" for e in data["markers"]["enemies"]
               if not data["walkable"][e["row"]][e["col"]]]
    check("semua musuh spawn di tile walkable", not bad_mob, "; ".join(bad_mob[:5]))

    # sungai memisahkan peta kecuali lewat jembatan
    bridge_rows = defaultdict(list)
    for y in range(h):
        for x in range(w):
            if data["tile_names"][data["ground_tiles"][y][x]].startswith("bridge_deck"):
                bridge_rows[y].append(x)
    check("ada jembatan di peta", any(len(v) >= 3 for v in bridge_rows.values()), str({k: len(v) for k, v in bridge_rows.items()}))
    bridge_cells = {(x, y) for y, xs in bridge_rows.items() for x in xs}
    start = (spawn["col"], spawn["row"])
    seen = {start}
    stack = [start]
    while stack:
        x, y = stack.pop()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if not (0 <= nx < w and 0 <= ny < h) or (nx, ny) in seen or not data["walkable"][ny][nx]:
                continue
            if (nx, ny) in bridge_cells:
                continue
            seen.add((nx, ny))
            stack.append((nx, ny))
    east = [(x, y) for y in range(h) for x in range(w)
            if data["walkable"][y][x] and data["terrain_names"][data["terrain"][y][x]] == "grass" and x > 82]
    reached = any((x, y) in seen for x, y in east)
    check("tanpa jembatan, tepi timur sungai terpisah", not reached, f"{sum(1 for c in east if c in seen)} sel terjangkau")

    zones = {z["id"] for z in data["markers"]["zones"]}
    check("empat zona terdefinisi", zones == {"greenwood_vale", "north_woods", "old_road", "ancient_ruins"}, str(sorted(zones)))

    npcs = data["markers"].get("npcs", [])
    check("tiga NPC desa terdefinisi", len(npcs) == 3, str(len(npcs)))
    bad_npc = [f"{n['id']}@{n['col']},{n['row']}" for n in npcs if not data["walkable"][n["row"]][n["col"]]]
    check("semua NPC berdiri di tile walkable", not bad_npc, "; ".join(bad_npc))
    too_close = [
        f"{a['id']}-{b['id']}"
        for i, a in enumerate(npcs) for b in npcs[i + 1:]
        if abs(a["col"] - b["col"]) < 3 and abs(a["row"] - b["row"]) < 3
    ]
    check("NPC tidak berdesakan", not too_close, "; ".join(too_close))
    blank_lines = [n["id"] for n in npcs if not n["lines"] or any(not line.strip() for line in n["lines"])]
    check("setiap NPC punya dialog", not blank_lines, "; ".join(blank_lines))
    quests = [n["quest"] for n in npcs if n.get("quest")]
    check("ada quest dengan hadiah XP",
          any(q["reward_xp"] > 0 and q["count"] >= 1 and q["flag"] for q in quests), str(len(quests)))

    # peti harta: solid (tidak bisa dipijak) tapi bisa didekati dari tile sebelah
    chests = data["markers"].get("chests", [])
    check("peti harta terdefinisi", len(chests) >= 4, str(len(chests)))
    check("setiap peti punya hadiah gold", all(c["gold"] > 0 for c in chests),
          str([c["gold"] for c in chests]))
    walkable_chests = [c["id"] for c in chests if data["walkable"][c["row"]][c["col"]]]
    check("tile peti tidak bisa dipijak (padat)", not walkable_chests, "; ".join(walkable_chests))
    unreachable = [
        c["id"] for c in chests
        if not any(0 <= c["col"] + dx < w and 0 <= c["row"] + dy < h and
                   data["walkable"][c["row"] + dy][c["col"] + dx]
                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
    ]
    check("peti bisa dicapai dari tile sebelah", not unreachable, "; ".join(unreachable))
    prop_at = {(p["col"], p["row"]): p["group"] for p in data["props"]}
    no_prop = [c["id"] for c in chests if prop_at.get((c["col"], c["row"])) != "chest_closed"]
    check("tiap marker peti punya prop chest_closed di tile itu", not no_prop, "; ".join(no_prop))


def verify_npcs() -> None:
    print("== atlas NPC ==")
    png = CHAR / "npc_atlas.png"
    meta_path = CHAR / "npc.json"
    check("npc: npc_atlas.png + npc.json ada", png.exists() and meta_path.exists())
    if not (png.exists() and meta_path.exists()):
        return
    meta = json.loads(meta_path.read_text())
    atlas = Image.open(png).convert("RGBA")
    cell = meta["cell"]
    check("npc: atlas = kolom x baris x sel",
          atlas.width == meta["atlas"]["columns"] * cell["w"] and
          atlas.height == meta["atlas"]["rows"] * cell["h"])
    check("npc: tiga warga, dua arah idle",
          len(meta["animations"]) == 3 and
          all(len(set(anim)) == 2 for npc in meta["animations"].values() for anim in npc.values()),
          str(len(meta["animations"])))
    outside = [name for name, f in meta["frames"].items()
               if f["x"] + f["w"] > atlas.width or f["y"] + f["h"] > atlas.height]
    check("npc: semua frame di dalam atlas", not outside, "; ".join(outside[:3]))
    empty = [name for name, f in meta["frames"].items()
             if cell_opaque(atlas, [f["x"], f["y"], f["w"], f["h"]], False)[0] == 0]
    check("npc: tidak ada frame kosong", not empty, "; ".join(empty[:3]))
    # tiap NPC harus beda palet (bukan sprite hasil copy)
    palettes = set()
    for name, f in meta["frames"].items():
        if not name.endswith("idle_down_1"):
            continue
        box = atlas.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))
        palettes.add(hash(box.tobytes()))
    check("npc: setiap warga punya palet sendiri", len(palettes) == 3, str(len(palettes)))


def verify_hero() -> None:
    print("== atlas hero ==")
    meta = json.loads((CHAR / "hero.json").read_text())
    atlas = Image.open(CHAR / "hero_atlas.png").convert("RGBA")
    a = meta["atlas"]
    check("ukuran atlas = kolom x baris x sel",
          atlas.size == (a["columns"] * meta["cell"]["w"], a["rows"] * meta["cell"]["h"]), f"{atlas.size}")
    check("jumlah frame = 53", meta["frame_count"] == 53, str(meta["frame_count"]))

    out_of_bounds = []
    empty = []
    for name, f in meta["frames"].items():
        if f["x"] + f["w"] > atlas.width or f["y"] + f["h"] > atlas.height:
            out_of_bounds.append(name)
        opaque, _ = cell_opaque(atlas, [f["x"], f["y"], f["w"], f["h"]], False)
        if opaque == 0:
            empty.append(name)
    check("semua frame di dalam atlas", not out_of_bounds)
    check("tidak ada frame kosong", not empty, "; ".join(empty[:5]))

    required = {"idle_down", "walk_down", "run_down", "idle_up", "walk_up", "run_up",
                "idle_side", "walk_side", "run_side", "attack", "skill", "hurt", "death", "jump"}
    have = set(meta["animations"])
    check("semua animasi wajib ada", required <= have, str(sorted(required - have)))
    empty_anim = [name for name, anim in meta["animations"].items() if not anim["frames"]]
    check("tidak ada animasi kosong", not empty_anim, "; ".join(empty_anim))

    # kaki semua frame berdiri sejajar garis tanah
    foot_y = meta["foot"]["y"]
    off_line = []
    for name, f in meta["frames"].items():
        im = atlas.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))
        box = im.getchannel("A").getbbox()
        if box is None:
            continue
        if not (foot_y - 1 <= box[3] <= foot_y + 2):
            off_line.append(f"{name} bottom={box[3]}")
    check("kaki sejajar garis tanah (toleransi 2 px)", not off_line, "; ".join(off_line[:4]))

    sources = {f["source"] for f in meta["frames"].values()}
    check("memakai frame AI + prosedural", sources == {"ai", "procedural"}, str(sources))


def verify_enemies() -> None:
    print("== atlas musuh ==")
    meta = json.loads((CHAR / "enemy.json").read_text())
    atlas = Image.open(CHAR / "enemy_atlas.png").convert("RGBA")
    a = meta["atlas"]
    check("ukuran atlas = kolom x baris x sel",
          atlas.size == (a["columns"] * meta["cell"]["w"], a["rows"] * meta["cell"]["h"]), f"{atlas.size}")
    check("tiga tipe musuh", set(meta["enemies"]) == {"slime", "bat", "warden"}, str(sorted(meta["enemies"])))
    required = {"idle", "move", "attack", "hurt", "death"}
    for enemy, info in meta["enemies"].items():
        have = set(info["animations"])
        check(f"animasi {enemy} lengkap", required <= have, str(sorted(required - have)))
        empty = [k for k, v in info["animations"].items() if not v]
        check(f"animasi {enemy} tidak kosong", not empty, "; ".join(empty))

    empty = []
    for key, f in meta["frames"].items():
        opaque, _ = cell_opaque(atlas, [f["x"], f["y"], f["w"], f["h"]], False)
        if opaque == 0:
            empty.append(key)
    check("tidak ada frame musuh kosong", not empty, "; ".join(empty[:5]))
    check("boss memakai frame AI", len([k for k in meta["frames"] if k.startswith("warden")]) == 9,
          str(len([k for k in meta["frames"] if k.startswith("warden")])))


def verify_font() -> None:
    """Font bitmap HUD: glyph lengkap, di dalam atlas, keras (tanpa anti-aliasing)."""
    png = FONT / "pixel.png"
    fnt = FONT / "pixel.fnt"
    check("font: pixel.png + pixel.fnt ada", png.exists() and fnt.exists())
    if not (png.exists() and fnt.exists()):
        return

    image = Image.open(png).convert("RGBA")
    lines = fnt.read_text(encoding="utf-8").splitlines()
    chars = [line for line in lines if line.startswith("char id=")]
    count_line = next((line for line in lines if line.startswith("chars count=")), "chars count=0")
    declared = int(count_line.split("=")[1])
    check("font: jumlah glyph = header", declared == len(chars), f"{declared} vs {len(chars)}")

    ids = {int(line.split("id=")[1].split(" ")[0]) for line in chars}
    needed_text = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?-+/()'\"%<>=*\u00b7\u2014"
    missing = sorted(set(needed_text) - {chr(i) for i in ids})
    check("font: semua glyph yang dipakai HUD tersedia", not missing, "".join(missing))

    outside = 0
    for line in chars:
        parts = dict(part.split("=") for part in line.split(" ")[1:])
        x, y = int(parts["x"]), int(parts["y"])
        w, h = int(parts["width"]), int(parts["height"])
        if x + w > image.width or y + h > image.height:
            outside += 1
    check("font: semua glyph di dalam atlas", outside == 0, f"{outside} keluar")

    data = list(image.getdata())
    check("font: alpha biner (tanpa anti-aliasing)", all(p[3] in (0, 255) for p in data))
    check("font: glyph putih murni (bisa diwarnai Label)",
          all(p[:3] == (255, 255, 255) for p in data if p[3] > 0))
    opaque = sum(1 for p in data if p[3] > 0)
    check("font: atlas benar-benar berisi glyph", opaque > 400, str(opaque))


def verify_audio() -> None:
    """SFX & musik harus benar-benar berisi suara, bukan file kosong."""
    print("== audio chiptune ==")
    manifest_path = AUDIO / "audio.json"
    check("audio: manifest audio.json ada", manifest_path.exists())
    if not manifest_path.exists():
        return

    manifest = json.loads(manifest_path.read_text())
    entries = {**manifest["sfx"], **manifest["music"]}
    check("audio: 9 SFX + 2 musik", len(manifest["sfx"]) == 9 and len(manifest["music"]) == 2,
          f"{len(manifest['sfx'])}+{len(manifest['music'])}")
    missing = [info["file"] for info in entries.values() if not (AUDIO / info["file"]).exists()]
    check("audio: semua file wav ada", not missing, "; ".join(missing[:3]))

    format_bad, silent = [], []
    for name, info in entries.items():
        path = AUDIO / info["file"]
        if not path.exists():
            continue
        with wave.open(str(path)) as handle:
            if handle.getnchannels() != 1 or handle.getsampwidth() != 2 or handle.getframerate() != 22050:
                format_bad.append(name)
            frames = handle.readframes(handle.getnframes())
        peak = max((abs(value) for value in struct.unpack(f"<{len(frames) // 2}h", frames)), default=0)
        if peak < 2000:
            silent.append(f"{name}={peak}")
    check("audio: format PCM 16-bit mono 22050 Hz", not format_bad, "; ".join(format_bad[:3]))
    check("audio: semua cue berisi suara (bukan senyap)", not silent, "; ".join(silent[:3]))

    looped = [name for name, info in manifest["music"].items() if info["loop"]]
    check("audio: musik ditandai loop", len(looped) == len(manifest["music"]), str(looped))
    check("audio: musik cukup panjang untuk loop",
          all(info["seconds"] >= 5 for info in manifest["music"].values()),
          str([info["seconds"] for info in manifest["music"].values()]))


def verify_export_preset() -> None:
    """Aset dibaca game sebagai byte mentah, jadi harus masuk include_filter ekspor."""
    print("== preset ekspor ==")
    preset = ROOT / "game/export_presets.cfg"
    check("ekspor: export_presets.cfg ada", preset.exists())
    if not preset.exists():
        return
    text = preset.read_text()
    match = re.search(r'include_filter="([^"]*)"', text)
    patterns = [p.strip() for p in (match.group(1).split(",") if match else []) if p.strip()]
    check("ekspor: include_filter terisi", bool(patterns), str(patterns))

    assets_cs = (ROOT / "game/scripts/Assets.cs").read_text()
    paths = re.findall(r'res://(assets/[^"]+)', assets_cs)
    check("ekspor: aset yang dibaca game terdeteksi", len(paths) >= 6, str(len(paths)))
    missing = [p for p in paths if not any(fnmatch.fnmatch(p, pattern) for pattern in patterns)]
    check("ekspor: semua aset mentah tercakup include_filter", not missing, "; ".join(missing[:5]))


def main() -> int:
    meta = verify_tileset()
    verify_world(meta)
    verify_hero()
    verify_enemies()
    verify_npcs()
    verify_font()
    verify_audio()
    verify_export_preset()
    print()
    print(f"=== aset: {CHECKS - len(FAILURES)} lulus, {len(FAILURES)} gagal ===")
    for failure in FAILURES:
        print(f"  GAGAL: {failure}")
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
