# Arga — Open World RPG (16-bit, top-down)

RPG solo bergaya **SNES JRPG**: satu region medieval fantasy (desa di tengah, sungai
berkelok + jembatan kayu di timur, hutan di utara, reruntuhan + mulut gua di barat
laut), karakter yang bisa maju/mundur/kiri/kanan, lari, menyerang, memakai skill, dan
**perjalanan tersimpan otomatis** ke backend C#.

| Bagian | Teknologi |
|---|---|
| Game (frontend) | **Godot 4.7 (.NET/C#)** — `game/` |
| Backend save | **ASP.NET Core minimal API + SQLite** — `server/RpgServer` |
| Aset | digenerate prosedural dengan Python + Pillow — `tools/` |

Tidak ada aset dari internet: tileset 16×16, peta, sprite karakter, dan sprite musuh
semuanya digambar dari nol oleh skrip di `tools/` (palet dari `PROMPTS.md`), sehingga
gaya, outline, shading, dan warnanya konsisten.

Game-nya **murni aplikasi Godot** — tidak ada frontend HTML/canvas. Backend C# hanya
melayani API save (plus `/assets` dan `/preview` untuk memeriksa hasil generator).

---

## 1. Jalankan game

```bash
bash scripts/setup.sh     # sekali: .NET 8 + Godot 4 lokal, generate aset, build
bash scripts/dev.sh       # nyalakan backend save, lalu buka jendela game Godot
```

`scripts/dev.sh` melakukan tiga hal:

1. build backend C#,
2. jalankan backend di `http://127.0.0.1:5080` (API profil/save + catatan perjalanan),
3. buka game Godot dengan `--server=http://127.0.0.1:5080`.

> Butuh layar (bukan headless). Untuk hanya menyalakan backend: `SERVER_ONLY=1 bash scripts/dev.sh`.

### Pratinjau & screenshot

```bash
python3 tools/preview_stages.py                       # SEBELUM/SESUDAH tiap tahap visual (deterministik)
bash scripts/shot.sh docs/shot-village.png            # screenshot asli dari jendela game (butuh layar)
bash scripts/shot.sh docs/shot-asap.png --shot-ash=0.45 --shot-delay=2
bash scripts/shot.sh docs/shot-malam.png --time=0.05 --shot-ash=0.3
```

### Kontrol

| Tombol | Aksi |
|---|---|
| `WASD` / `←↑↓→` | gerak 4 arah (maju, mundur, kiri, kanan) |
| `Shift` | lari (menguras napas) |
| `J` | serang (pedang) |
| `Spasi` | lompat (frame pose `jump_1`→`jump_3`) |
| `K` | skill **Aura Slash** — terbuka di level 3, memakai energi |
| `E` | bicara dengan NPC · buka peti harta · istirahat di api unggun (HP+napas penuh, otomatis simpan) |
| `R` | semprot air: padamkan api sekitar (biaya napas; pompa memperluas) |
| `C` | crafting: tas + resep (bahan dari peti: kain/tali/arang) |
| `M` | pakai peta sketsa: buka fog sekitar |
| `B` | pasang sekat api (memutus jalaran api) |
| `N` | tanam benih di tanah bekas terbakar |
| `Q` | minum potion (+40 HP) |
| `F` | simpan cepat ke slot terakhir |
| `Esc` | menu: simpan/muat 3 slot, catatan perjalanan, ganti profil, keluar |

### Alur bermain & penyimpanan

Pilih profil di menu awal (dibuat di backend) → main → **autosave tiap 30 detik** dan
setiap kali checkpoint dinyalakan, zona berganti, atau boss dikalahkan. Reload/reopen
game → pilih profil → `resume` mengembalikan slot terakhir: posisi, level, XP, HP,
skill, zona, checkpoint, waktu bermain, jumlah kill/mati. Server juga menyusun
**catatan perjalanan** otomatis (level up, checkpoint, zona, boss, kematian).

---

## 2. Isi dunia

* **Desa (Greenwood Vale)** — rumah kayu/batu dengan atap rumbia & sirap, cerobong,
  jendela, pintu, **sumur** di alun-alun batu, pasar, tong, peti, lampu, tiang penunjuk.
* **Old Road (timur)** — jalan tanah bertambal cobblestone menuju **jembatan kayu**
  yang menyeberangi sungai berkelok; satu-satunya jalur ke tepi timur.
* **North Woods (utara)** — hutan lebat oak & pinus (dua ukuran), semak, batu, tunggul.
* **Ancient Ruins (barat laut)** — plato batu, pilar runtuh, obor, peti harta, dan
  **mulut gua** yang menembus tebing.
* 4 zona itu terdeteksi otomatis dari posisi pemain; 3 checkpoint (alun-alun, jembatan,
  reruntuhan) jadi titik simpan/bangkit.

Musuh: **slime** (prosedural), **kelelawar** (prosedural), dan boss **Ruin Warden**
(frame AI yang kamu taruh di `assets/character/frames/enemies/`, dibersihkan + disamakan
palet & skalanya).

NPC desa — 5 warga dari generator top-down yang sama, masing-masing punya palet
dan **fitur pembeda** (tanpa pedang/zirah bawaan; gaya rambut, topi, celemek,
tongkat, dan metrik badan anak):

* **Mira** (pedagang pasar) — memberi quest *Bunuh 5 slime* (hadiah 120 XP, flag
  `quest_slime_done` tersimpan). Penanda **!** di atas kepala selama quest belum selesai.
  Rambut disanggul + celemek dagang.
* **Bram** (penjaga gerbang) — petunjuk jembatan & reruntuhan; satu-satunya warga
  yang berpedang + zirah bahu.
* **Sari** (warga) — lore desa + cara istirahat; rambut putih panjang, bertongkat.
* **Lena** (petani ladang, baru) — tutur soal tanah hangus yang menghijau lagi;
  topi jerami + celemek.
* **Tomas** (anak desa, baru) — mengagumi Bram; badan lebih pendek & ramping.

Semua warga punya animasi idle **dan walk** (2+4 frame per arah) — walk dipakai
untuk warga berjalan mondar-mandir di tahap berikutnya.

Dialog tampil di kotak bawah ala JRPG; permainan berhenti selama dialog, `E` untuk
lanjut. Semua NPC menghadap pemain saat diajak bicara.

**Ekonomi sederhana**: 5 **peti harta** tersebar (desa, jalan timur, hutan, gua,
reruntuhan) berisi **gold + 1 potion**; membuka peti menyimpan flag jadi tidak bisa
diambil dua kali dan sprite-nya berganti ke `chest_open`. Gold dipakai untuk membeli
potion di menu (`Esc` → "Beli potion — 25 gold"), potion diminum dengan `Q`.
Gold, potion, dan peti yang sudah dibuka ikut tersimpan ke server maupun ke simpanan
lokal.

---

## 3. Struktur proyek

```
game/                      proyek Godot 4 (.NET)
  project.godot            viewport 480x270 (di-scale 2x), filter piksel = nearest
  scenes/Main.tscn         root; seluruh dunia dibangun dari kode
  scripts/
    Game.cs                composition root + facade: argumen CLI, urutan _Process,
                           penyambung event; logika per domain ada di systems/
    systems/               (M1) EventBus, WorldManager, SpawnSystem, CombatSystem,
                           ProgressSystem, DialogSystem, InteractionSystem,
                           SaveSystem, TimeSystem, UiManager, CameraRig
    World.cs               TileMapLayer tanah, prop (y-sort), CollisionGrid, SpriteFrames
    Actors.cs              Player (4 arah, lari, serang, skill, lompat, hurt, mati) + Mob (AI)
    Npc.cs                 warga desa: sprite idle, penanda quest, menghadap pemain
    Rules.cs               statistik, kurva level, damage, tipe musuh
    SaveApi.cs             klien REST ke backend C# (otomatis jatuh ke simpanan lokal)
    LocalSave.cs           simpanan offline user://save_offline.json kalau server mati
    Audio.cs               parser WAV + pemutar 8 suara & 1 kanal musik
    Hud.cs                 HUD pixel + jam dunia, tracker quest, vignette HP rendah,
                           menu pause/profil + kotak dialog + minimap
    Fx.cs                  pipeline FX: asap, bloom tipis, color grading, heat haze
    Lighting.cs            siklus siang/malam + PointLight2D hangat di titik api
    Particles.cs           pooling partikel: nyala 4-frame, bara, abu, tunas pulih
    GameFeel.cs            game feel: camera shake, hit-stop, debu langkah, percikan
    FogOfWar.cs            overlay chunk belum dijelajahi + MinimapFog di peta mini
    FireVisuals.cs         nyala api gameplay (pool sprite) + bara
    RainOverlay.cs         hujan layar penuh saat cuaca hujan
    Assets.cs / Data.cs    pemuat PNG & parser JSON aset
    HeadlessTest.cs        tes yang menjalankan kode game sungguhan
  assets/                  hasil generate (tileset, peta, atlas) + tekstur FX + shader
  assets/data/             items.json (barang & resep), objectives.json (rantai misi)

server/RpgServer/          ASP.NET Core + SQLite (profil, 3 slot save, resume, journey)
  Endpoints/               Health, Profile, Save, Journey (minimal API per domain)
  Data/GameDb.cs           SQLite: skema, meta schema_version, CRUD, backup VACUUM INTO
  Validation/StateValidator  validasi/normalisasi GameState v1
  Contracts/               DTO request + bentuk error {error}
  appsettings.json         Rpg:Host/Port/DatabasePath (env RPG_* tetap menang)
server/RpgServer.Tests/    harness unit konsol tanpa NuGet (dijalankan test-all)
tools/                     generator aset + verifikator
   generate_tileset.py     194 tile / 206 cell: rumput, jalan (autotile 16), cobble,
                           air + tepi, tebing, pohon, rumah, atap, jembatan, sumur, gua,
                           tanah hangus (3 varian untuk zona bekas terbakar)…
   generate_world_map.py   susun peta 96x72 tile + NPC + preview + grid walkability
   generate_topdown_frames.py  40 frame hero 4 arah (idle/walk/run)
   generate_pixel_font.py  font bitmap 5x7 (BMFont) untuk HUD
   generate_audio.py       9 SFX + 2 musik chiptune (WAV PCM, disintesis sendiri)
   generate_fx.py          tekstur FX: noise asap tileable, cahaya radial, titik glow,
                           lembar nyala 4 frame, tunas zona terbakar & vignette HUD
   preview_stages.py       pratinjau SEBELUM/SESUDAH tiap tahap visual -> docs/
   build_hero_sprites.py   gabung frame prosedural + frame AI (attack/skill/hurt/death/jump)
   build_enemy_sprites.py  slime & bat prosedural + boss dari frame AI
   build_npc_sprites.py    5 warga desa (palet + fitur pembeda; Tomas berbadan anak)
   verify_assets.py        verifikasi artefak (tanpa perlu mata manusia)
   generate_character_frames.py / make_spritesheet.py   pipeline side-view lama (masih
                           dipakai untuk 3 frame pelengkap + ekspor .tres Godot)
tests/                     tes HTTP backend (node --test), tanpa browser
   api.test.mjs             kontrak API: profil, 3 slot save, resume, journey, clamping,
                           + penjaga "server tidak menyajikan HTML"
   api-persistence.test.mjs  dibuktikan setelah server direstart (SQLite)
game/assets/font/          pixel.png + pixel.fnt (font HUD, ikut diekspor lewat
                           include_filter di game/export_presets.cfg)
docs/                      GDD.md (desain "Arga: Api & Asap"), ARCHITECTURE.md (peta
                           modul & kontrak sistem), pratinjau (tileset, peta, hero,
                           musuh, NPC, tahap visual preview-stage-*) — bisa dibuka di
                           http://127.0.0.1:5080/preview/preview-world.png
```

### Bagaimana aset dipakai

```
tools/*.py  ->  game/assets/{tiles,world,character}/*.{png,json}
                     |
                     +-> gambar dibaca apa adanya (tanpa importer) via Assets.LoadTexture
                     +-> data tile/prop/animation dibaca dari JSON (satu sumber kebenaran)
```

Peta direpresentasikan sebagai data: `overworld.json` memuat tile ground per sel
(hasil autotile), daftar prop, **grid walkability**, zona, checkpoint, titik spawn
musuh, dan **zona bekas terbakar** (dekoratif — abu & tunas, bukan gameplay).
`CollisionGrid` memakai grid itu untuk tabrakan (per-sumbu), jadi gerak pemain
deterministik dan bisa diuji tanpa physics engine.

---

## 4. Tes

```bash
bash scripts/test-all.sh
```

Yang dijalankan:

1. **generate aset** (idempoten) lalu **`tools/verify_assets.py`** — 101 pemeriksaan
   artefak: tidak ada tile saling menimpa/keluar atlas, tidak ada tile kosong,
   alpha biner (tanpa anti-aliasing), palet terbatas, collision cell cocok, grid
   walkable konsisten dengan terrain + prop, spawn/checkpoint/musuh/NPC di tile yang bisa
   dipijak, sungai benar-benar terpisah kecuali lewat jembatan, semua frame sprite di
   dalam atlas, kaki sejajar garis tanah, animasi tidak ada yang kosong, font pixel
   lengkap & hard-edged, tiap NPC punya palet sendiri, peti padat tapi bisa dicapai,
   audio PCM 16-bit yang benar-benar berisi suara, tekstur FX (noise tileable, cahaya
   radial, nyala 4 frame & tunas ber-alpha biner, vignette HUD transparan di tengah,
   shader termuat, uniform `night`),
   tanah hangus (3 varian, gelap), zona bekas terbakar (di dalam peta + cukup tile
   walkable), dan `include_filter` ekspor mencakup semua aset mentah yang dibaca game.
2. **build backend C#**, **unit test server** (`server/RpgServer.Tests`, harness konsol
   tanpa NuGet: StateValidator, GameDb, `schema_version`, backup `VACUUM INTO`), dan
   **build game Godot C#**.
3. **tes API** (`tests/api.test.mjs`) + **persistensi setelah server direstart**
   (`tests/api-persistence.test.mjs`). Termasuk penjaga bahwa server tidak lagi
   menyajikan HTML/halaman web: `/` harus JSON, `/index.html` & `/prototype.html` 404.
4. **tes headless Godot** (`godot --headless --path game -- --test`) — menjalankan kode
   game yang sama seperti dimainkan: gerak 4 arah & flip, animasi idle/walk/run,
   serangan & skill (terkunci di level 1, aktif di level 3, memakai energi), lompat,
   kematian pemain (animasi `death` benar-benar diputar) & bangkit, damage ke musuh
   sampai mati → kill/XP/level, deteksi 4 zona & checkpoint, menyeberang sungai lewat
   jembatan (dan tidak bisa menembus air), serta save/load lewat backend sungguhan.
   Plus NPC: dialog terbuka/tertutup, baris quest, hadiah XP, flag quest tersimpan,
   penanda "!" hilang setelah selesai; font pixel termuat; simpanan offline; bayangan
   ambient/obor yang berkedip terpasang; pipeline FX (asap, bloom, grading, heat haze
   yang mengikuti 9 titik api) terpasang dengan uniform yang benar; lighting:
   9 PointLight2D di titik api, ambient siang terang / malam kebiruan, jam berputar &
   membungkus, serta uniform `night` grading ikut siklus; dan ambience api: nyala
   4-frame per titik api, bara yang kembali ke pool, abu hanya saat kamera di zona
    terbakar (+boost asap), tunas tumbuh lalu kembali ke pool; tanah hangus (overlay
    tilemap dekoratif di 3 zona terbakar — tepi ragged, hanya menimpa grass/meadow
    sehingga jalan & air tetap bersih) dan riak air per sel (offset fase dari hash,
    tidak pop serentak); game feel 2f: shake mereda & offset kamera dibatasi,
    hit-stop membekukan lalu berakhir, serangan kena otomatis memicu shake/hit-stop/
    percikan, debu langkah & percikan peti kembali ke pool (tidak pernah melebihi
    kapasitas), serta warga mondar-mandir (`walk_side`, tidak menjauh dari rumah,
    berhenti saat dialog/menu); HUD 2g: jam dunia mengikuti fase (Pagi/Siang/Senja/
    Malam), tracker quest tampil-baru-hilang sesuai progres & flag, vignette kedip
    saat terluka lalu mereda, denyut vignette saat HP < 30%, dan menu jeda memuat
    ringkasan profil. Seksi "sistem M1" memastikan EventBus, Spawn/Combat/Progress/
    Dialog/Interaction/WorldManager terpasang, event `mob_killed`/`zone_changed`
    tercatat deterministik, dan facade dialog/save tetap sama. Keluar dengan exit
    code ≠ 0 kalau ada yang gagal.

---

## 5. Catatan & batasan

* Game dijalankan dari sumber (`--path game`). Kalau nanti di-*export*, PNG aset perlu
  di-import Godot atau ikut diekspor (kode membaca PNG apa adanya via `FileAccess`).
* Progresi bersifat client-authoritative (game solo lokal): server memvalidasi bentuk
  data dan membatasi nilai, bukan mengawasi permainan.
* API tidak memakai autentikasi — memang untuk main solo di mesin sendiri.
* Kalau backend mati, game tetap bisa dimainkan: profil & 3 slot disimpan di
  `user://save_offline.json` (tombol/menu sama). Saat server hidup lagi, profil lokal
  itu **otomatis dipindah** ke server (nama yang sama dilewati) dan file lokal
  dihapus setelah kosong.
* Visual: air memakai 16 tile fase-2 (`water_<mask>_b`) + 3 varian air dalam;
  fase tiap sel di-offset dari hash koordinat (`World._waterOffset`), jadi riak
  menjalar dari sel ke sel alih-alih seluruh sungai "pop" serentak (tiap 0,6 detik),
  obor/lentera berkedip, ada bayangan ambient di bawah pohon/bangunan, dan minimap di
  kanan bawah HUD.
* Pipeline FX (tahap visual "Api & Asap") berjalan di `CanvasLayer` sendiri (layer 5,
  di bawah HUD): asap dua lapis dari noise tileable + color grading/vignette + bloom
  tipis + heat haze di 9 titik api (obor, lentera, api unggun). Semuanya dekoratif —
  tidak mengubah gameplay. Tekstur FX di `assets/fx/` sengaja **bukan** pixel-art
  biner (alpha halus untuk partikel), di luar cek alpha biner tileset. Pratinjau
  sebelum/sesudah: `python3 tools/preview_stages.py`; screenshot asli yang dirender:
  `bash scripts/shot.sh docs/shot.png` (butuh layar).
* Siklus siang/malam (`Lighting.cs`): satu hari penuh 5 menit — `CanvasModulate`
  menggelapkan dunia perlahan (malam kebiruan), `PointLight2D` hangat berkedip di
  9 titik api dan makin terang saat malam, grading ikut menyesuaikan. HUD & menu tidak
  ikut digelapkan. Mulai di jam tertentu: `--time=0..1` (0 = tengah malam,
  0.5 = tengah hari), atur kecepatan: `--day-length=detik`.
* Ambience api (`Particles.cs`, punya sendiri: pooling, tanpa alokasi saat main):
  nyala api 4 frame beranimasi di tiap titik api, bara naik terbawa angin lembut,
  dan di 3 **zona bekas terbakar** (data `markers.burnt`) ada abu beterbangan + asap
  sedikit menebal saat kamera di zona itu, plus tunas hijau tumbuh-memudar (hutan
  pulih) di atas tanah hangus (overlay `Scorched`, `World.cs`). Semuanya dekoratif —
  walkable, damage, dan AI tidak berubah.
* Audio: 9 SFX (langkah, pedang, kena, tumbang, level, quest, istirahat, skill) +
  2 musik loop (desa & padang liar) — semuanya disintesis `tools/generate_audio.py`.
* Game feel (`GameFeel.cs`, tahap 2f — murni visual): kamera bergetar singkat saat
  pukulan kena / pemain terluka (offset dibatasi 4 px agar tidak mual), hit-stop
  ±55–85 ms membuat pukulan terasa berbobot (freeze frame dunia, HUD tetap hidup),
  debu kecil di kaki saat melangkah/berlari & saat mendarat dari lompat, percikan
  saat musuh kena dan saat peti dibuka. Warga desa kini mondar-mandir pendek di
  sekitar rumahnya memakai frame `walk_side` (berhenti saat dialog/menu terbuka).
  Tidak ada perubahan pada damage, kolisi, atau AI.
* HUD & menu (`Hud.cs`, tahap 2g): jam dunia kecil di kanan atas mengikuti
  `Lighting.TimeOfDay` (fase Pagi/Siang/Senja/Malam + HH:MM), tracker quest
  menampilkan quest aktif beserta progres kill ("Mira: kalahkan slime 3/5") dan
  berubah/hilang saat selesai, vignette (`fx_vignette.png`) dipakai dua fungsi —
  kedip merah singkat saat pemain terluka dan denyut lembut saat HP < 30% — serta
  ringkasan profil (level, gold, kill, tumbang, playtime, zona) di menu jeda.
  Semuanya murni informasi, tidak mengubah gameplay.
* Menutup jendela game atau menekan "Keluar" memicu autosave terlebih dahulu.
* Ekspor (`game/export_presets.cfg`) menyertakan PNG/JSON/font mentah lewat
  `include_filter`, karena game membacanya sebagai byte; ekspor itu sendiri belum diuji
  di mesin ini (butuh template ekspor Godot).
* Aset AI yang kamu taruh di `assets/character/frames/` sudah dibersihkan otomatis
  (alpha dibinerkan, warna di-snap ke palet Arga, bintik dibuang, kaki disejajarkan).
  Kalau kamu regenerate frame AI, cukup jalankan `python3 tools/build_hero_sprites.py`
  dan `python3 tools/build_enemy_sprites.py` lagi.
* `assets/character/build/arga_frames.tres` (ekspor Godot dari pipeline side-view lama)
  dibuat oleh `tools/make_spritesheet.py` dan tidak dipakai game top-down ini.

### Pipeline side-view lama (masih hidup)

Dipakai untuk 3 frame pelengkap pose aksi (`jump_1`, `jump_2`, `death_2`) dan untuk
membuat ulang `hero-reference.png` / `.tres` Godot:

```bash
python3 tools/generate_character_frames.py                 # 23 frame side-view 88x96
python3 tools/make_spritesheet.py --target-height 64       # spritesheet + atlas + .tres
python3 tools/make_spritesheet.py --only idle,walk --strict
```

Opsi `make_spritesheet.py`: `--only`, `--cell`, `--columns`, `--align`, `--tolerance`,
`--no-chroma-key`, `--no-despill`, `--strict`, `--target-height 0` (tanpa resize).
