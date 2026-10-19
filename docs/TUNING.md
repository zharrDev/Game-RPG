# TUNING — Parameter yang Boleh Diubah

Dokumen ini memuat angka penyetelan yang dipakai gameplay, lokasi datanya, dan
efeknya. Aturan Fase 2: jangan menaruh angka penyetelan sebagai literal di kode;
taruh di JSON data atau konstanta bernama, lalu catat di sini.

Setelah mengubah angka: jalankan `bash scripts/test-all.sh`. Tes headless memakai
nilai dari JSON yang sama, jadi hasil tuning langsung ikut teruji.

## F1 — Musim Kemarau (`game/assets/data/fire_season.json`)

| Kunci | Nilai awal | Efek | Catatan |
|---|---:|---|---|
| `day_count` | 14 | Panjang musim (hari) | HUD `Hari X/14`; hari berhenti di angka ini |
| `day_seconds` | 300 | Detik nyata per hari | 5 menit/hari → musim ±70 menit |
| `wildfire.first_day` | 2 | Hari pertama api liar menyala | Jadwal tetap bisa mulai lebih awal |
| `wildfire.max_per_day` | 2 | Batas titik api liar per hari | Mencegah layar penuh api |
| `wildfire.intensity` | 0.8 | Intensitas awal sel api liar | 0..1; makin tinggi makin cepat menyebar |
| `wildfire.schedule` | 6 hari | Situs kurasi per hari `[kolom, baris]` | Harus walkable & berbahan bakar |
| `wildfire.pool` | 8 situs | Kandidat untuk hari tanpa jadwal | Dipilih hash deterministik |
| `village.rect` | `[48,36,16,12]` | Kotak desa untuk radius ancaman | Dalam tile |
| `village.threat_tiles` | 8 | Jarak (Chebyshev) api dianggap mengancam | Dari tepi kotak desa |
| `village.drain_per_cell_sec` | 2.4 | Kuras meter desa per sel api/detik | 1 sel ≈ −2.4/tik |
| `village.recover_per_sec` | 0.8 | Pemulihan meter desa/detik saat aman | |
| `village.evacuate_below` | 60 | Ambang warga non-inti mengungsi | Bram, Sari, Tomas |
| `village.full_evacuate_below` | 30 | Ambang semua warga mengungsi | Termasuk Mira & Lena |
| `village.return_above` | 75 | Ambang semua warga kembali | Histeresis 60–75 menahan status |
| `forest.drain_per_cell_sec` | 0.5 | Kuras kesehatan hutan per sel api/detik | |
| `forest.recover_per_sec` | 0.6 | Pemulihan hutan/detik saat padam | |
| `forest.sprout_bonus` | 3 | Bonus hutan per tunas yang ditanam | Dari counter `restored` |
| `rain.health_threshold` | 70 | Hutan ≥ nilai ini mendorong hujan | Di bawahnya bias hujan = 0 |
| `rain.bias` | 0.35 | Dorongan stage hujan (0..1) | 0.35 → peluang hujan ±55% per stage |
| `rain.energy_bonus` | 0.5 | Tambahan skala regen energi saat hujan | 1 + 0.5 = 50% lebih cepat |
| `mask.smoke_factor` | 0.35 | Pengali kuras asap saat memakai masker | 0.35 = menahan 65% |
| `ending.meters_floor` | 0.65 | Lantai skor ending dari meter | Lihat GDD bagian 8 |

### Musuh & NPC — jangan diubah tanpa regenerasi atlas

- Jumlah NPC tetap **5** (Mira, Bram, Sari, Lena, Tomas). Menambah NPC mengharuskan
  regenerasi `npc_atlas.png` + `npc.json` (`tools/build_npc_sprites.py`).
- Salt hash cuaca `77/78/91` dan `WeatherSystem.Hash01` adalah bagian determinisme;
  mengubahnya menggeser seluruh jadwal cuaca/hari.

## Tombol Debug (Fase 2)

| Tombol | Fungsi |
|---|---|
| `F1` | Maju satu hari (menyalakan api liar terjadwal) |
| `F2` | Sulut satu situs api liar dari pool (bergilir deterministik) |
| `F3` | Placeholder Echo kematian (aktif di F2) |
| `F4` | Overlay performa: FPS, posisi, zona, hari, sel api, meter |

Tombol debug hanya bekerja saat bermain (kecuali F4 yang selalu bisa). Diuji di
`HeadlessTest` lewat fungsi sistemnya (`DebugNextDay`, `DebugIgnite`), bukan
penekanan tombol fisik.

## Lain-lain

- `Rules.AutosaveInterval`, `Rules.MaxEnergy`, `Rules.SmokeEnergyDrain`,
  `Rules.DeathDuration`: `game/scripts/Rules.cs`.
- Harga potion & hadiah misi: `assets/data/items.json` dan
  `assets/data/objectives.json`.
- Jadwal insiden bukti: `assets/data/incidents.json` (jangan ubah id/flag; tes
  M6–M8 mengunci rantainya).
