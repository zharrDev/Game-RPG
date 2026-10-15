# Open World RPG Pixel Art — Pipeline Aset Karakter

Proyek karakter pixel art side-view (medieval fantasy, warrior) untuk engine **Godot 4**.

Dokumen prompt & character bible ada di [`PROMPTS.md`](./PROMPTS.md) (dokumen 2 dari 3).
Repo ini berisi bagian teknisnya: struktur folder, config karakter, dan tooling perakit
spritesheet yang dipakai di bagian 5 PROMPTS.md.

## Struktur

```
PROMPTS.md                              # character bible + 23 prompt frame
requirements.txt                        # Pillow (dependency tool)
assets/character/
  character.json                        # palet, daftar 23 frame, fps/loop per animasi
  reference/hero-reference.png          # ⬅ taruh reference image di sini (image-to-image)
  frames/<nama>.png                     # ⬅ hasil generate + cleanup, mis. idle_1.png
  build/arga_spritesheet.png            # hasil tool (generated)
  build/arga_spritesheet.json           # metadata atlas (generated)
  build/arga_frames.tres                # resource SpriteFrames Godot 4 (generated)
tools/make_spritesheet.py               # perakit spritesheet
```

Folder `build/` boleh dihapus dan dibuat ulang kapan saja — semuanya generated.

## Alur kerja

1. **Generate frame** — pakai prompt per frame di PROMPTS.md bagian 3 (attach
   `reference/hero-reference.png` sebagai image-to-image, mis. Leonardo strength 0.35–0.5
   atau SD img2img denoise 0.45–0.65 + ControlNet OpenPose).
2. **Simpan** hasilnya ke `assets/character/frames/<nama>.png` dengan nama yang **persis**
   sama dengan daftar di `character.json` (`idle_1.png`, `walk_1.png`, … `death_3.png`).
3. **Cleanup manual** di Aseprite/Piskel: samakan tinggi badan, posisi kaki, dan palet
   antar frame (wajib — hasil AI selalu ada jitter).
4. **Rakit** sprite sheet:
   ```bash
   python3 -m pip install -r requirements.txt
   python3 tools/make_spritesheet.py --target-height 64
   ```
   Tool akan melaporkan frame mana yang belum ada, jadi kamu bisa mengerjakan
   bertahap: `run_4`, `jump_*`, `attack_*`, `skill_*`, `hurt_1`, `death_*`.
5. **Import ke Godot 4** (lihat bawah).

## Perintah tools/make_spritesheet.py

```bash
# default: baca assets/character/character.json, semua frame yang ada, tinggi badan 64px
python3 tools/make_spritesheet.py

# kerjakan hanya sebagian animasi (frame lain belum digenerate)
python3 tools/make_spritesheet.py --only idle,walk,run --target-height 64

# paksa ukuran cell & grid (mis. untuk tileset/sprite 64x64)
python3 tools/make_spritesheet.py --cell 64x64 --columns 5

# gagal kalau masih ada frame yang kurang (cocok untuk CI / build final)
python3 tools/make_spritesheet.py --strict

# tanpa resize, pakai frame apa adanya (kalau sudah digenerate di ukuran final)
python3 tools/make_spritesheet.py --target-height 0
```

Opsi penting:

| Opsi | Default | Fungsi |
|------|---------|--------|
| `--target-height` | `64` (dari config) | Tinggi badan pada frame acuan `scale_ref` (`idle_1`). `0` = tanpa resize. |
| `--tolerance` | `48` | Toleransi jarak warna ke `#FF00FF` sebelum dianggap background. |
| `--no-chroma-key` | off | Matikan penghapusan background magenta. |
| `--no-despill` | off | Matikan pengurangan fringe magenta di tepi sprite. |
| `--align` | `bottom-center` | Penempatan konten di cell: `bottom-center`, `bottom-left`, `center`. |
| `--resample` | `auto` | `auto` = NEAREST saat memperbesar, BOX saat mengecilkan. |
| `--padding` | `1` | Margin transparan di dalam cell (ruang untuk outline). |
| `--only` | — | Batasi animasi, mis. `--only idle,walk`. |
| `--strict` | off | Exit code ≠ 0 kalau ada frame hilang. |

### Anti-jitter (penting)

- Semua frame diskalakan dengan **faktor yang sama**, dihitung dari frame acuan
  (`scale_ref`, default `idle_1`), bukan per frame. Kalau tiap frame diskalakan sendiri,
  tinggi badan berubah-ubah dan animasi terlihat goyang.
- Setiap cell berukuran identik dan konten **rata bawah** di dalam cell, jadi kaki tetap
  menapak di tanah pada semua frame (termasuk `jump_*` dan `death_2` yang rebah).
- `--target-height` diukur dari tinggi *konten* frame acuan — pastikan `idle_1.png` adalah
  pose berdiri penuh (seluruh badan terlihat) supaya skalanya benar.

## Import ke Godot 4

1. Copy folder `assets/` ke dalam project Godot (`res://assets/character/...`).
2. Godot otomatis meng-import `arga_spritesheet.png` (pertahankan **Filter: off** dan
   **Compress: Lossless** di tab Import supaya tetap tajam).
3. `arga_frames.tres` sudah berupa resource `SpriteFrames`. Cara pakai:
   - **AnimatedSprite2D** → Sprite Frames → Load → `arga_frames.tres`, animate otomatis
     dari `Sprite Frames` inspector.
   - Atau di node `AnimatedSprite2D` set `Animation = idle` di script:
     ```gdscript
     @onready var anim: AnimatedSprite2D = $AnimatedSprite2D

     func _physics_process(_delta: float) -> void:
         anim.play("walk")            # idle, walk, run, jump, attack, skill, hurt, death
         # menghadap kiri: cukup flip, tidak perlu frame terpisah
         anim.flip_h = velocity.x < 0
     ```
4. Setiap region AtlasTexture memakai **rect cell penuh** (bukan rect konten), sehingga
   semua frame berukuran sama dan `AnimatedSprite2D` tidak jitter. Karena itu set
   `Offset`/pivot node ke titik kaki, mis. `offset = Vector2(0, -cell_h / 2)` dengan
   `centered` disesuaikan kebutuhan.

`arga_spritesheet.json` juga tersedia (berisi rect cell + rect konten per frame, ukuran
sheet, dan daftar animasi) kalau kamu butuh memprosesnya di tool lain atau engine lain.

## Status frame (23 frame, 1 arah — hadap kanan)

| Animasi | Frame | fps | Loop |
|---------|-------|-----|------|
| idle | idle_1, idle_2 | 6 | ✅ |
| walk | walk_1 … walk_4 | 10 | ✅ |
| run | run_1, run_2, run_3, run_4 | 12 | ✅ |
| jump | jump_1, jump_2, jump_3 | 10 | — |
| attack | attack_1, attack_2, attack_3 | 12 | — |
| skill | skill_1, skill_2, skill_3 | 10 | — |
| hurt | hurt_1 | 12 | — |
| death | death_1, death_2, death_3 | 6 | — |

Untuk menghadap kiri cukup **flip horizontal** di engine — tidak perlu generate frame
tambahan.

## Catatan

- Background magenta `#FF00FF` dihapus otomatis, termasuk fringe di tepi sprite.
  Kalau hasil AI-nya punya gradien/bayangan, aktifkan cleanup manual dulu di Aseprite.
- Edit `character.json` kalau mau ubah fps, urutan frame, palet, atau menambah animasi.
