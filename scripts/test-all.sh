#!/usr/bin/env bash
# Uji seluruh proyek:
#   1. generate aset (idempoten)
#   2. verifikasi artefak aset (tileset/peta/sprite)
#   3. build backend C#
#   4. build game Godot (C#)
#   5. tes API end-to-end + kontrak save (tests/)
#   6. restart server -> tes persistensi
#   7. tes headless Godot: game benar-benar dijalankan (gerak, tabrakan, animasi, tempur, save)
#
# Pemakaian:
#   bash scripts/test-all.sh
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

PORT="${RPG_TEST_PORT:-5099}"
DB="/tmp/rpg-test-$$.db"
MARKER="/tmp/rpg-marker-$$.json"
export RPG_BASE_URL="http://127.0.0.1:$PORT"
export RPG_MARKER_FILE="$MARKER"
export RPG_DB="$DB"
export RPG_PORT="$PORT"

GODOT_BIN="$(ls "$ROOT"/.godot-bin/Godot_v*_mono_linux.x86_64 2>/dev/null | head -1 || true)"

SERVER_PID=""
cleanup() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  rm -f "$DB" "$DB-wal" "$DB-shm" "$DB.bak" "$MARKER"
}
trap cleanup EXIT

start_server() {
  dotnet run --project server/RpgServer --no-build -- --port "$PORT" >/tmp/rpg-server.log 2>&1 &
  SERVER_PID=$!
  for _ in $(seq 1 60); do
    if curl -sf "$RPG_BASE_URL/api/health" >/dev/null 2>&1; then return 0; fi
    sleep 0.5
  done
  echo "server gagal start; log:" >&2
  tail -20 /tmp/rpg-server.log >&2
  return 1
}

stop_server() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  SERVER_PID=""
}

PY="$(rpg_python "$ROOT")" || {
  echo "Pillow belum ada — jalankan dulu: bash scripts/setup.sh" >&2
  exit 1
}

echo "== 1/7 generate aset (tileset 16x16, peta, sprite, font, audio, fx) =="
"$PY" tools/generate_tileset.py
"$PY" tools/generate_pixel_font.py
"$PY" tools/generate_audio.py
"$PY" tools/generate_fx.py
"$PY" tools/generate_world_map.py
"$PY" tools/build_hero_sprites.py
"$PY" tools/build_enemy_sprites.py
"$PY" tools/build_npc_sprites.py
"$PY" tools/preview_stages.py

echo "== 2/7 verifikasi artefak aset =="
"$PY" tools/verify_assets.py

echo "== 3/7 build backend C# =="
dotnet build server/RpgServer/RpgServer.csproj -v quiet --nologo

echo "== 3b/7 unit test server (harness konsol, tanpa NuGet) =="
dotnet run --project server/RpgServer.Tests -v quiet --nologo

echo "== 4/7 build game Godot (C#) =="
dotnet build game/RpgGame.csproj -v quiet --nologo

echo "== 5/7 tes API + kontrak save (server jalan) =="
start_server
(cd tests && node --test api.test.mjs)

echo "== 6/7 restart server, lalu tes persistensi =="
stop_server
start_server
(cd tests && node --test api-persistence.test.mjs)

echo "== 7/7 tes headless Godot (game dijalankan sungguhan) =="
if [[ -z "$GODOT_BIN" ]]; then
  echo "   Godot tidak ada di ./.godot-bin — jalankan: bash scripts/setup.sh" >&2
  exit 1
fi
"$GODOT_BIN" --headless --path game -- --test "--server=$RPG_BASE_URL"

echo
echo "SEMUA TES LULUS ✅"
