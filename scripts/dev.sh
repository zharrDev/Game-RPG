#!/usr/bin/env bash
# Jalankan game: nyalakan backend save (C# + SQLite) lalu buka game Godot 4.
#
#   bash scripts/dev.sh                  # backend :5080 + game Godot
#   RPG_PORT=8080 bash scripts/dev.sh
#   SERVER_ONLY=1 bash scripts/dev.sh    # hanya backend (dipakai tes/skrip lain)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
# shellcheck source=scripts/python.sh
source "$ROOT/scripts/python.sh"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="$ROOT/.dotnet-home"
export DOTNET_ROOT="$ROOT/.dotnet"
export PATH="$ROOT/.dotnet:$PATH"

PORT="${RPG_PORT:-5080}"
BASE="http://127.0.0.1:$PORT"
GODOT_BIN="$(ls "$ROOT"/.godot-bin/Godot_v*_mono_linux.x86_64 2>/dev/null | head -1 || true)"

if [[ ! -x "$ROOT/.dotnet/dotnet" ]]; then
  echo "SDK .NET belum ada di ./.dotnet — jalankan dulu: bash scripts/setup.sh" >&2
  exit 1
fi

[[ -f game/assets/world/overworld.json && -f game/assets/font/pixel.fnt && -f game/assets/audio/audio.json ]] || {
  echo "Aset game belum ada. Menjalankan generator..." >&2
  PY="$(rpg_python "$ROOT")" || { echo "Pillow belum ada — jalankan: bash scripts/setup.sh" >&2; exit 1; }
  "$PY" tools/generate_tileset.py >/dev/null
  "$PY" tools/generate_pixel_font.py >/dev/null
  "$PY" tools/generate_audio.py >/dev/null
  "$PY" tools/generate_world_map.py >/dev/null
  "$PY" tools/build_hero_sprites.py >/dev/null
  "$PY" tools/build_enemy_sprites.py >/dev/null
  "$PY" tools/build_npc_sprites.py >/dev/null
}

echo "== build backend =="
dotnet build server/RpgServer/RpgServer.csproj -v quiet --nologo

echo "== nyalakan backend $BASE =="
RPG_PORT="$PORT" dotnet run --project server/RpgServer --no-build -- --port "$PORT" >/tmp/rpg-server.log 2>&1 &
SERVER_PID=$!
cleanup() {
  kill "$SERVER_PID" 2>/dev/null || true
  wait "$SERVER_PID" 2>/dev/null || true
}
trap cleanup EXIT

for _ in $(seq 1 60); do
  curl -sf "$BASE/api/health" >/dev/null 2>&1 && break
  sleep 0.5
done
if ! curl -sf "$BASE/api/health" >/dev/null 2>&1; then
  echo "backend gagal start; log:" >&2
  tail -20 /tmp/rpg-server.log >&2
  exit 1
fi
echo "   backend siap (save game + catatan perjalanan): $BASE"

if [[ "${SERVER_ONLY:-0}" == "1" ]]; then
  echo "   SERVER_ONLY=1 — menunggu. Ctrl+C untuk berhenti."
  wait "$SERVER_PID"
  exit 0
fi

if [[ -z "$GODOT_BIN" ]]; then
  echo "Godot belum ada di ./.godot-bin — jalankan: bash scripts/setup.sh" >&2
  exit 1
fi

echo "== build game Godot (C#) =="
dotnet build game/RpgGame.csproj -v quiet --nologo

echo
echo "  🎮  Membuka game Godot…"
echo "      WASD/panah gerak · Shift lari · J serang · K skill (Lv 3) · Spasi lompat · E istirahat · F simpan · Esc menu"
echo
"$GODOT_BIN" --path game -- --server="$BASE"
