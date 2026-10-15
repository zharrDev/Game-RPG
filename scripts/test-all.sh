#!/usr/bin/env bash
# Uji seluruh proyek: build server C#, tes logika game (node), tes API end-to-end,
# restart server untuk membuktikan persistensi, lalu (opsional) tes browser.
#
# Pemakaian:
#   bash scripts/test-all.sh              # tanpa tes browser
#   RUN_BROWSER=1 bash scripts/test-all.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="$ROOT/.dotnet-home"
export PATH="$ROOT/.dotnet:$PATH"

PORT="${RPG_TEST_PORT:-5099}"
DB="/tmp/rpg-test-$$.db"
MARKER="/tmp/rpg-marker-$$.json"
export RPG_BASE_URL="http://127.0.0.1:$PORT"
export RPG_MARKER_FILE="$MARKER"
export RPG_DB="$DB"
export RPG_PORT="$PORT"

SERVER_PID=""
cleanup() {
  if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  rm -f "$DB" "$DB-wal" "$DB-shm" "$MARKER"
}
trap cleanup EXIT

start_server() {
  "$ROOT/.dotnet/dotnet" run --project server/RpgServer --no-build -- --port "$PORT" >/tmp/rpg-server.log 2>&1 &
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

echo "== 1/5 build backend C# =="
"$ROOT/.dotnet/dotnet" build server/RpgServer/RpgServer.csproj -v quiet --nologo

echo "== 2/5 tes logika game (web/test/core.test.mjs) =="
(cd web && node --test test/core.test.mjs)

echo "== 3/5 tes API + kontrak save (server jalan) =="
start_server
(cd web && node --test test/api.test.mjs)

echo "== 4/5 restart server, lalu tes persistensi =="
stop_server
start_server
(cd web && node --test test/api-persistence.test.mjs)

echo "== 5/5 tes browser headless (opsional) =="
if [[ "${RUN_BROWSER:-0}" == "1" ]]; then
  (cd web && RUN_BROWSER=1 RPG_BASE_URL="$RPG_BASE_URL" node --test test/browser.test.mjs)
else
  echo "   dilewati (set RUN_BROWSER=1 untuk menjalankan)"
fi

echo
echo "SEMUA TES LULUS ✅"
