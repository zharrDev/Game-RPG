# Arsitektur — Arga: Api & Asap

Klien Godot 4.7 (.NET 8) + backend C# ASP.NET minimal API + SQLite.
Tujuan dokumen: peta modul, kontrak antar-sistem, dan aturan yang menjaga
perubahan tetap aman (dites otomatis).

```
┌──────────────────────────────┐        HTTP/JSON        ┌──────────────────────────┐
│ Godot client (game/)         │ ──────────────────────▶ │ RpgServer (server/)      │
│  Game.cs  = composition root │  /health /profiles       │  Endpoints/ (minimal)    │
│  systems/ = logika per domain│  /save /journey ...      │  Validation/StateValid.  │
│  Hud/Fx/Lighting/Feel = view │ ◀────────────────────── │  Data/GameDb + SQLite    │
└──────────────────────────────┘                         └──────────────────────────┘
```

## 1. Klien Godot

### Game.cs — composition root (facade)

`Game` adalah satu-satunya god-node yang tersisa. Tanggung jawabnya:

1. Parse argumen CLI (`--server`, `--profile`, `--slot`, `--test`, `--shot`, ...).
2. Membangun komponen view: `World`, `Player`, `Hud`, `Fx`, `Lighting`,
   `FireAmbience`, `GameFeel`, `AudioPlayer`.
3. Membuat sistem (di bawah) dan menyambungkan event-nya.
4. Meneruskan input & urutan `_Process` (lihat bagian 3).
5. Menyediakan **facade publik** yang stabil untuk `HeadlessTest` dan skrip:
   `Interact()`, `ApplyStrike()`, `BuildState()`, `SaveOnExit()`, dst.

Aturan: sistem tidak membuat node baru di `_Ready` sendiri; semuanya dikonfigurasi
eksplisit lewat konstruktor/`Configure()` dari `Game`. Ini menjaga urutan
inisialisasi deterministik dan headless-testable.

### systems/ — logika per domain

| Sistem | Tanggung jawab | Dipakai oleh |
|--------|----------------|--------------|
| `EventBus` | event bertipe + counter deterministik | semua sistem |
| `WorldManager` | zona, checkpoint, prop tile, heat fx attach | Game, Interaction |
| `SpawnSystem` | spawn/tick mob & NPC, marker quest | Game, Progress |
| `CombatSystem` | serangan pemain, damage masuk, respawn, tick mob | Game |
| `ProgressSystem` | XP/level, kill, reward quest, flag | Dialogue, Spawn |
| `DialogSystem` | buka/baris/tutup dialog, grant reward | Interaction |
| `InteractionSystem` | tombol E: NPC/peti/potion/istirahat | Game |
| `SaveSystem` | build state, autosave, slot, profil, journey | banyak |
| `TimeSystem` | jam dunia, kedip obor, fase riak air | Game |
| `UiManager` | pause menu, journal, quest tracker, HUD update | Game |
| `CameraRig` | kamera + offset game feel | Game |
| `ChunkSystem` | grid chunk 8x8 tile: aktivasi aktor + fog of war | Combat, Spawn, Fog |
| `WeatherSystem` | cuaca (kemarau/berangin/hujan), arah & kekuatan angin | Fire, HUD |
| `FireSystem` | api grid: bahan bakar, kelembapan, penjalaran angin, asap, sekat | Weather, Game |
| `InventorySystem` | tas (GameState.Inventory), craft resep data-driven | Peti, Misi, HUD |
| `ObjectiveSystem` | rantai misi data-driven; cek item/craft/explore/extinguish/flag/counter | Inventory, Fire, HUD |
| `IncidentSystem` | banyak insiden data-driven: mulai per zona/jarak, api, bukti, konfrontasi, papan bukti (V) | Fire, Dialog, Misi |
| `FireVisuals` / `RainOverlay` | nyala api gameplay & hujan layar | Fire, Hud |
| `FogOfWar` / `MinimapFog` | overlay chunk belum dijelajahi (dunia & peta mini) | Game, Hud |

Kontrak `EventBus` (M1):

```
MobKilled(mob)        CombatSystem → ProgressSystem (hadiah), UiManager (tracker)
PlayerDamaged(mob,hp) CombatSystem → Game (Feel shaked/hitstop, Hud.FlashDamage)
QuestFlagSet(flag)    DialogSystem/ProgressSystem → UiManager (tracker), Spawn (marker)
ZoneChanged(id,name)  WorldManager → UiManager, SaveSystem (autosave)
SaveWritten(slot)     SaveSystem → UiManager (toast/journal)
```

Aturan: pengiriman event **hanya** dari sistem yang memiliki kejadian; efek
visual (shake, partikel) dipasang oleh `Game` saat menyambung event, bukan di
dalam sistem. Ini yang membuat `CombatSystem` bisa dites tanpa HUD.

### Urutan `_Process` (setelah M1)

```
Feel.Advance          → hit-stop & shake (paling awal, memutus tick lain)
Lighting.Advance      → jam dunia
Ambience.Advance      → bara/abu/tunas (visual)
Weather.Advance       → cuaca & angin (hanya saat bermain)
Fire.Advance          → penjalaran api + konsumsi bahan bakar
Fire.SmokeAt          → paparan asap pemain (sebelum Player.Tick)
input & dialog        → UiManager/InteractionSystem
Player.Tick           → gerak, napas, aksi, strike (event)
Fire.TickPlayer       → damage api, sesak asap
CombatSystem.TickMobs → mob berpikir/menyerang
SpawnSystem.TickNpcs  → warga mondar-mandir
TimeSystem.Advance    → kedip & riak
UiManager.Update      → HUD, tracker, cuaca, pause
```

Aturan api (M4): api hanya menyala lewat `FireSystem.IgniteCell/At` (nanti dipicu
event M6). Bahan bakar dari `terrain` (rumput/meadow 1, dirt 0.4, sand 0.15,
air/batu 0). Angin mempercepat penjalaran searah, hujan menaikkan kelembapan +
memadamkan. `R` menyemprot radius `Rules.ExtinguishRange` dengan biaya napas;
berlari menguras napas; asap menahan regen dan menguras napas.

## 2. Backend (server/RpgServer)

```
Program.cs                 bootstrap: opsi, db, middleware, map endpoints (<60 baris)
appsettings.json           Rpg:DatabasePath, Rpg:Port (env RPG_DB/RPG_PORT menang)
Contracts/Requests.cs      DTO request (CreateProfile, SaveRequest, ...)
Endpoints/HealthEndpoints  /health (+ schemaVersion)
Endpoints/ProfileEndpoints /profiles, /profiles/{id}/resume
Endpoints/SaveEndpoints    /save, /load, /save/{id}/{slot}
Endpoints/JourneyEndpoints /journey, /events
Data/GameDb.cs             SQLite: skema, migrasi, CRUD, VACUUM INTO backup
Validation/StateValidator  validasi GameState v1 (kompatibel Save v2 nanti)
```

Prinsip:
- `Program.cs` hanya merangkai; logika ada di Endpoints/Data/Validation.
- Skema DB dikelola `GameDb` dengan tabel `meta(key, value)`;
  `schema_version` mulai `1` (groundwork migrasi Save v2 di M8).
- Endpoint mutasi (`POST/PUT/DELETE`) dibatasi rate limiter (terpasang M1,
  limit longgar) supaya klien liar tidak membebani SQLite.
- Saat startup, DB yang sudah ada dibackup `VACUUM INTO rpg.db.bak`
  (satu langkah, overwrite) — pemulihan cepat bila migrasi M8 bermasalah.

## 3. Kontrak State & Versi

- `world.inventory` menyimpan tas (id slug, jumlah 0..99, maks 40 jenis);
  dinormalisasi server dan ikut semua save.
- `world.explored` menyimpan indeks chunk yang sudah dijelajahi (fog of war).
  Nilainya dinormalisasi server (unik, urut, maks 2048) dan ikut semua save.
- `state.version == "1"` sekarang; Save v2 (M8) menaikkan ke `"2"` dengan
  migrasi otomatis `v1 → v2` di `StateValidator` **dan** di klien
  (`SaveApi.Load`). Tes lama harus tetap lulus tanpa perubahan bentuk.
- Bentuk error tetap `{ "error": "..." }`; health tetap `{ ok, version, ... }`
  dengan tambahan `schemaVersion` (aditif, tidak menghapus field lama).

## 4. Determinisme & Seni Prosedural

- Semua generator (`tools/generate_*.py`) dan runtime memakai **Hash01 yang
  identik** (paritas C#–Python): salt 77/78/91 untuk tepi scorched, varian
  scorched, offset riak air. Jangan ubah tanpa memperbarui kedua sisi.
- PNG preview (`tools/preview_stages.py`) adalah kanari refactor: setelah
  perubahan non-visual, hash gambar harus byte-identik.

## 5. Strategi Tes (scripts/test-all.sh)

1. `verify_assets.py` — pin angka aset & budget palet (≤96 warna, alpha biner).
2. Headless Godot (`--test`) — skenario gameplay, termasuk seksi `sistem`
   (bus, spawn, simpan, dst) dan pin determinisme.
3. `api.test.mjs` / `api-persistence.test.mjs` — backend HTTP end-to-end.
4. `server/RpgServer.Tests` — unit test konsol (tanpa NuGet):
   `StateValidator` + `GameDb` + `schema_version`.
5. Preview decode sanity (PNG valid & nilai pin) tanpa menyentuh Godot.

Aturan praktis: setiap perubahan kode wajib lulus `bash scripts/test-all.sh`
sebelum commit, dan setiap ekstraksi sistem harus **tidak mengubah** output
preview (murni refactor).
