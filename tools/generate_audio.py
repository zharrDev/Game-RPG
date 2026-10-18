#!/usr/bin/env python3
"""Buat SFX & musik chiptune prosedural untuk game (tanpa aset dari internet).

Semua nada disintesis: gelombang kotak (square) dengan envelope, plus noise
untuk pukulan/langkah — gaya 16-bit. Hasilnya WAV PCM 16-bit mono 22050 Hz di
`game/assets/audio/` + `audio.json` (manifest yang dibaca Audio.cs).

Jalankan:
    python3 tools/generate_audio.py
"""

from __future__ import annotations

import json
import math
import random
import struct
import sys
import wave
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
OUT = ROOT / "game" / "assets" / "audio"

RATE = 22050
TAU = math.tau


def silence(seconds: float) -> list[float]:
    return [0.0] * int(RATE * seconds)


def square(freq: float, seconds: float, volume: float, duty: float = 0.5,
           attack: float = 0.005, release: float = 0.03) -> list[float]:
    """Nada kotak dengan envelope linear sederhana."""
    total = max(1, int(RATE * seconds))
    sample: list[float] = []
    period = RATE / max(1.0, freq)
    attack_samples = max(1, int(RATE * attack))
    release_samples = max(1, int(RATE * release))
    for i in range(total):
        if math.fmod(i, period) / period < duty:
            value = volume
        else:
            value = -volume
        if i < attack_samples:
            value *= i / attack_samples
        remaining = total - i
        if remaining < release_samples:
            value *= remaining / release_samples
        sample.append(value)
    return sample


def noise(seconds: float, volume: float, seed: int = 1, decay: bool = True) -> list[float]:
    rng = random.Random(seed)
    total = max(1, int(RATE * seconds))
    sample: list[float] = []
    for i in range(total):
        value = rng.uniform(-volume, volume)
        if decay:
            value *= 1.0 - i / total
        sample.append(value)
    return sample


def sweep(start: float, end: float, seconds: float, volume: float, duty: float = 0.5) -> list[float]:
    total = max(1, int(RATE * seconds))
    sample: list[float] = []
    phase = 0.0
    for i in range(total):
        t = i / total
        freq = start + (end - start) * t
        phase += freq / RATE
        value = volume if math.fmod(phase, 1.0) < duty else -volume
        sample.append(value * (1.0 - t))
    return sample


def mix(*tracks: list[float]) -> list[float]:
    length = max(len(track) for track in tracks)
    out = [0.0] * length
    for track in tracks:
        for i, value in enumerate(track):
            out[i] += value
    return out


def concat(*tracks: list[float]) -> list[float]:
    out: list[float] = []
    for track in tracks:
        out.extend(track)
    return out


def write_wav(name: str, samples: list[float]) -> dict:
    path = OUT / f"{name}.wav"
    frames = bytearray()
    for value in samples:
        clipped = max(-1.0, min(1.0, value))
        frames.extend(struct.pack("<h", int(clipped * 32767)))
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(RATE)
        handle.writeframes(bytes(frames))
    duration = len(samples) / RATE
    return {"file": f"{name}.wav", "seconds": round(duration, 3), "bytes": len(frames)}


# --------------------------------------------------------------------------- #
# SFX
# --------------------------------------------------------------------------- #
def build_sfx() -> dict[str, list[float]]:
    return {
        # langkah: klik pendek (dipakai tiap ~0.3 detik saat berjalan)
        "step": mix(noise(0.035, 0.06, seed=3), square(180, 0.03, 0.05, duty=0.25)),
        # ayunan pedang
        "slash": mix(sweep(1400, 420, 0.11, 0.16, duty=0.3), noise(0.08, 0.05, seed=7)),
        # musuh kena pukulan
        "hit": mix(sweep(320, 90, 0.10, 0.2, duty=0.5), noise(0.09, 0.12, seed=11)),
        # pemain kena
        "hurt": mix(sweep(520, 180, 0.16, 0.18, duty=0.5), noise(0.12, 0.07, seed=13)),
        # pemain tumbang
        "death": concat(square(392, 0.12, 0.16, duty=0.5), square(311, 0.12, 0.16, duty=0.5),
                        square(233, 0.16, 0.16, duty=0.5), square(155, 0.34, 0.18, duty=0.25)),
        # naik level: arpeggio naik
        "levelup": concat(square(523, 0.09, 0.14, duty=0.5), square(659, 0.09, 0.14, duty=0.5),
                          square(784, 0.09, 0.14, duty=0.5), square(1047, 0.22, 0.16, duty=0.5)),
        # quest selesai: denting dua nada
        "quest": concat(square(880, 0.10, 0.13, duty=0.25), silence(0.03),
                        square(1175, 0.10, 0.13, duty=0.25), silence(0.02),
                        square(1568, 0.22, 0.12, duty=0.25)),
        # istirahat di checkpoint
        "rest": concat(square(392, 0.10, 0.10, duty=0.5), square(523, 0.10, 0.10, duty=0.5),
                       square(659, 0.18, 0.10, duty=0.5)),
        # skill aura slash
        "skill": mix(sweep(300, 1600, 0.22, 0.15, duty=0.25), noise(0.2, 0.06, seed=17)),
    }


# --------------------------------------------------------------------------- #
# Musik loop (8 birama, pentatonik, bass sederhana)
# --------------------------------------------------------------------------- #
def note(freq: float, beat: float, volume: float, duty: float = 0.5) -> list[float]:
    return square(freq, beat, volume, duty=duty)


def build_track(scale: list[float], bass: list[float], beat: float, melody: list[int],
                volume: float, seed: int, hat: bool) -> list[float]:
    melody_track: list[float] = []
    for index, step in enumerate(melody):
        if step is None:
            melody_track.extend(silence(beat / 2))
        else:
            freq = scale[step % len(scale)]
            melody_track.extend(note(freq, beat / 2 * 0.92, volume, duty=0.25))
    bass_track: list[float] = []
    for root in bass:
        bass_track.extend(note(root, beat, volume * 0.75, duty=0.125))
    tracks = [melody_track, bass_track]
    if hat:
        percussion: list[float] = []
        for _ in range(int(len(melody_track) / (RATE * beat / 2)) + 1):
            percussion.extend(noise(0.02, 0.025, seed=seed, decay=True))
            percussion.extend(silence(beat / 2 - 0.02))
        tracks.append(percussion)
    return mix(*tracks)


def build_music() -> dict[str, list[float]]:
    # A minor pentatonic: A C D E G
    a_minor = [220.0, 261.63, 293.66, 329.63, 392.0, 440.0, 523.25, 587.33]
    village_melody = [0, 2, 4, 2, 5, 4, 2, None, 3, 4, 5, 4, 2, 0, None, None,
                      2, 4, 5, 7, 5, 4, 2, None, 4, 2, 0, 2, 4, None, None, None]
    village_bass = [110.0, 110.0, 130.81, 98.0, 110.0, 130.81, 98.0, 110.0]
    village = build_track(a_minor, village_bass, 0.42, village_melody, 0.085, seed=23, hat=False)

    # padang liar: lebih rendah & lambat, tanpa hat
    wild_scale = [146.83, 174.61, 196.0, 220.0, 261.63, 293.66, 349.23, 392.0]
    wild_melody = [0, None, 2, None, 4, 2, 0, None, 3, None, 4, None, 2, 0, None, None,
                   4, 5, 4, 2, 0, None, 2, None, 0, None, None, None, None, None, None, None]
    wild_bass = [73.42, 73.42, 87.31, 65.41, 73.42, 87.31, 65.41, 73.42]
    wilds = build_track(wild_scale, wild_bass, 0.5, wild_melody, 0.075, seed=31, hat=True)
    return {"music_village": village, "music_wilds": wilds}


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    manifest: dict[str, dict] = {"sample_rate": RATE, "sfx": {}, "music": {}}

    for name, samples in build_sfx().items():
        info = write_wav(name, samples)
        manifest["sfx"][name] = {**info, "loop": False}
    for name, samples in build_music().items():
        # jaga durasi loop tetap utuh (musik diputar berulang oleh AudioStreamWAV)
        info = write_wav(name, samples)
        manifest["music"][name] = {**info, "loop": True}

    (OUT / "audio.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"audio: {len(manifest['sfx'])} SFX, {len(manifest['music'])} musik -> {OUT}")
    for group in ("sfx", "music"):
        for name, info in manifest[group].items():
            print(f"  {name:14s} {info['seconds']:5.2f}s  {info['bytes'] // 1024:4d} KB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
