#!/usr/bin/env bash
# Setup sekali jalan: SDK .NET lokal, Godot 4 (.NET) lokal, dependency Python,
# lalu generate semua aset game (tileset, peta, sprite hero & musuh) dan build.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
# shellcheck source=scripts/python.sh
source "$ROOT/scripts/python.sh"

GODOT_VERSION="${GODOT_VERSION:-4.7.2-stable}"
GODOT_DIR="$ROOT/.godot-bin"
GODOT_BIN="$GODOT_DIR/Godot_v${GODOT_VERSION%-*}-stable_mono_linux.x86_64"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="$ROOT/.dotnet-home"

echo "== 1/6 SDK .NET 8 (lokal di ./.dotnet) =="
if [[ -x "$ROOT/.dotnet/dotnet" ]]; then
  echo "   sudah ada: $("$ROOT/.dotnet/dotnet" --version)"
else
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel 8.0 --install-dir "$ROOT/.dotnet" --no-path
fi

echo "== 2/6 Godot 4 (.NET, build mono) lokal di ./.godot-bin =="
if [[ -x "$GODOT_BIN" ]]; then
  echo "   sudah ada: $("$GODOT_BIN" --headless --version)"
else
  mkdir -p "$GODOT_DIR" /tmp/godot-dl
  ZIP="/tmp/godot-dl/Godot_v${GODOT_VERSION}_mono_linux_x86_64.zip"
  if [[ ! -f "$ZIP" ]]; then
    curl -sSL -o "$ZIP" "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}/Godot_v${GODOT_VERSION}_mono_linux_x86_64.zip"
  fi
  unzip -q -o "$ZIP" -d /tmp/godot-dl
  cp -r "/tmp/godot-dl/Godot_v${GODOT_VERSION}_mono_linux_x86_64/." "$GODOT_DIR/"
  chmod +x "$GODOT_DIR"/*.x86_64
fi

echo "== 3/6 dependency Python (Pillow) =="
if ! PY="$(rpg_python_setup "$ROOT")"; then
  echo "Pillow dibutuhkan untuk generate aset. Pasang manual lalu ulangi setup.sh:" >&2
  echo "   Debian/Ubuntu : sudo apt install python3-pil python3-venv" >&2
  echo "   lain-lain     : pip install --user Pillow" >&2
  exit 1
fi
echo "   pakai $PY ($("$PY" -c 'import PIL; print("Pillow " + PIL.__version__)'))"

echo "== 4/6 generate aset: tileset 16x16 + font pixel + audio =="
"$PY" tools/generate_tileset.py
"$PY" tools/generate_pixel_font.py
"$PY" tools/generate_audio.py

echo "== 5/6 generate aset: peta overworld + sprite hero, musuh & NPC =="
"$PY" tools/generate_world_map.py
"$PY" tools/build_hero_sprites.py
"$PY" tools/build_enemy_sprites.py
"$PY" tools/build_npc_sprites.py
"$PY" tools/verify_assets.py

echo "== 6/6 build backend C# + game Godot =="
export PATH="$ROOT/.dotnet:$PATH"
"$ROOT/.dotnet/dotnet" build server/RpgServer/RpgServer.csproj -v quiet --nologo
"$ROOT/.dotnet/dotnet" build game/RpgGame.csproj -v quiet --nologo

echo
echo "Selesai. Jalankan game:"
echo "   bash scripts/dev.sh                 # backend save + game Godot"
echo "   bash scripts/test-all.sh            # seluruh tes"
