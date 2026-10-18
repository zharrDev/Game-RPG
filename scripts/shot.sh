#!/usr/bin/env bash
# Ambil satu screenshot dari game yang benar-benar dirender (butuh layar —
# Godot headless tidak merender) lalu simpan sebagai PNG, mis. ke docs/.
#
#   bash scripts/shot.sh docs/shot-village.png
#   bash scripts/shot.sh docs/shot-asap-tebal.png --shot-ash=0.45 --shot-delay=2
#   bash scripts/shot.sh docs/shot-malam.png --time=0.05
#
# Opsi yang diteruskan ke game:
#   --shot-delay=N   detik tunggu sebelum menangkap (default 1.5)
#   --shot-ash=N     ketebalan asap 0..0.75 (default 0.20)
#   --time=N         jam mulai 0..1 (0 = tengah malam, 0.5 = tengah hari)
#   --day-length=N   durasi sehari penuh dalam detik (default 300)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="$ROOT/.dotnet-home"
export DOTNET_ROOT="$ROOT/.dotnet"
export PATH="$ROOT/.dotnet:$PATH"

OUT="${1:?pemakaian: bash scripts/shot.sh <file.png> [--shot-delay=N] [--shot-ash=N]}"
shift

GODOT_BIN="$(ls "$ROOT"/.godot-bin/Godot_v*_mono_linux.x86_64 2>/dev/null | head -1 || true)"
if [[ -z "$GODOT_BIN" ]]; then
  echo "Godot belum ada di ./.godot-bin — jalankan: bash scripts/setup.sh" >&2
  exit 1
fi
if [[ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
  echo "Tidak ada layar (DISPLAY/WAYLAND_DISPLAY kosong) — screenshot asli butuh display." >&2
  echo "Pakai pratinjau deterministik: python3 tools/preview_stages.py" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUT")"
OUT_ABS="$(readlink -f "$(dirname "$OUT")")/$(basename "$OUT")"

echo "== build game Godot (C#) =="
dotnet build game/RpgGame.csproj -v quiet --nologo

echo "== tangkap layar -> $OUT_ABS =="
"$GODOT_BIN" --path game -- "--shot=$OUT_ABS" "$@"
echo "screenshot: $OUT_ABS"
