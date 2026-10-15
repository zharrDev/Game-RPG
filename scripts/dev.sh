#!/usr/bin/env bash
# Jalankan game: build backend C# lalu serve game + API di satu port.
#
#   bash scripts/dev.sh            # http://127.0.0.1:5080
#   RPG_PORT=8080 bash scripts/dev.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="$ROOT/.dotnet-home"
PORT="${RPG_PORT:-5080}"

if [[ ! -x "$ROOT/.dotnet/dotnet" ]]; then
  echo "SDK .NET belum ada di ./.dotnet — jalankan dulu: bash scripts/setup.sh" >&2
  exit 1
fi

if [[ ! -f assets/character/build/arga_spritesheet.png ]]; then
  echo "Spritesheet belum ada, generate dulu: python3 tools/generate_character_frames.py && python3 tools/make_spritesheet.py"
fi

"$ROOT/.dotnet/dotnet" build server/RpgServer/RpgServer.csproj -v quiet --nologo

echo
echo "  🎮  Buka browser: http://127.0.0.1:$PORT"
echo "      Kontrol: ←/→ atau A/D jalan · Shift lari · Spasi lompat · J serang · K skill · F simpan cepat · Esc menu"
echo
exec "$ROOT/.dotnet/dotnet" run --project server/RpgServer --no-build -- --port "$PORT"
