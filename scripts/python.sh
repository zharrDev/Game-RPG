#!/usr/bin/env bash
# Helper bersama (di-source oleh scripts lain): pilih interpreter Python yang
# punya Pillow. Urutan: python3 sistem -> ./.venv -> bikin ./.venv baru.
#
# Dipakai karena banyak distro (Debian/Ubuntu/Fedora) memblokir `pip install`
# ke Python sistem (PEP 668), jadi kalau Pillow belum ada kita bikin venv lokal.
# Semua pesan diagnostik ditulis ke stderr supaya stdout hanya berisi path.

rpg_python() {
  local root="$1"
  if python3 -c "import PIL" >/dev/null 2>&1; then
    echo "python3"
    return 0
  fi
  if [[ -x "$root/.venv/bin/python" ]] && "$root/.venv/bin/python" -c "import PIL" >/dev/null 2>&1; then
    echo "$root/.venv/bin/python"
    return 0
  fi
  return 1
}

# Sama seperti rpg_python, tapi kalau belum ada Pillow dia bikin ./.venv lalu pasang.
rpg_python_setup() {
  local root="$1"
  local found
  if found="$(rpg_python "$root")"; then
    echo "$found"
    return 0
  fi
  echo "   Pillow belum ada — membuat virtualenv di ./.venv (Python sistem tidak diubah)" >&2
  if ! python3 -m venv "$root/.venv" >/dev/null 2>&1; then
    echo "   gagal membuat virtualenv (di Debian/Ubuntu butuh paket python3-venv)" >&2
    return 1
  fi
  "$root/.venv/bin/python" -m pip install --quiet --upgrade pip >/dev/null 2>&1 || true
  if ! "$root/.venv/bin/python" -m pip install --quiet -r "$root/requirements.txt" >&2; then
    echo "   gagal memasang Pillow ke ./.venv" >&2
    return 1
  fi
  echo "$root/.venv/bin/python"
}
