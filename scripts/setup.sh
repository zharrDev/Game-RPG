#!/usr/bin/env bash
# Setup sekali jalan: SDK .NET lokal, dependency Python, sprite karakter, lalu build server.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="$ROOT/.dotnet-home"

echo "== 1/4 SDK .NET 8 (lokal di ./.dotnet) =="
if [[ -x "$ROOT/.dotnet/dotnet" ]]; then
  echo "   sudah ada: $("$ROOT/.dotnet/dotnet" --version)"
else
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel 8.0 --install-dir "$ROOT/.dotnet" --no-path
fi

echo "== 2/4 dependency Python (Pillow) =="
python3 -m pip install --quiet --user -r requirements.txt || python3 -m pip install --quiet -r requirements.txt

echo "== 3/4 sprite karakter (23 frame prosedural + spritesheet) =="
python3 tools/generate_character_frames.py
python3 tools/make_spritesheet.py

echo "== 4/4 build backend C# =="
"$ROOT/.dotnet/dotnet" build server/RpgServer/RpgServer.csproj -v quiet --nologo

echo
echo "Selesai. Jalankan game dengan: bash scripts/dev.sh"
