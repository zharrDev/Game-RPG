# GDD — "Arga: Api & Asap"

> Survival–investigation open world, pixel art 16-bit top-down, 1 pemain.
> Dokumen ini adalah sumber kebenaran desain; perubahan gameplay mengacu ke sini.
> Status implementasi per milestone ada di bagian Roadmap (bawah).

## 1. Premis

Musim kemarau panjang. Hutan Greenwoods terbakar serentak di banyak titik —
bukan kebakaran alami, tapi **pembakaran sengaja** untuk membuka lahan.

Arga, pemuda penjaga hutan desa, mengambil peran **juru padam + penyelidik**:
memadamkan api, menyelamatkan warga, mengumpulkan bukti, dan mengungkap siapa
dalang di balik "Rangga Kelabu" — nama yang muncul di nota-nota bahan bakar.

Nada cerita: hangat tapi membara. Warga punya sisi baik dan sisi panik;
tersangka punya alasan yang masuk akal; kebenaran tidak hitam-putih, tapi
pilihan akhir pemain menentukan nasib hutan.

## 2. Core Loop

1. **Patroli** satu Pos Pantau; baca angin + cuaca + kondisi bahan bakar.
2. **Padamkan** api kecil sebelum membesar; selamatkan properti/warga.
3. **Kumpulkan bukti** dari titik api yang dipadamkan (jerigen, nota, korek).
4. **Tanyai warga**; beberapa hanya bicara setelah bukti/kondisi tertentu.
5. **Pulihkan** hutan: tanam tunas di tanah terbakar (N).
6. **Konfrontasi** tersangka tiap Pos → nama dalang terungkap → pos berikutnya;
   serahkan berkas ke Mira untuk memicu ending.

Loop pendek (2–4 menit) = padam/pulihkan satu kejadian.
Loop panjang (satu pos) = 20–30 menit: bukti + kesaksian → tersangka pos.
Sesi penuh (5 pos + finale) = 4–6 jam.

## 3. Sistem Inti

- **Api & angin** (M4): api menjalar mengikuti bahan bakar, kelembapan tile,
  dan arah angin. Angin berubah dengan cuaca; api besar menciptakan asap yang
  menutup pandangan dan menyulitkan pemain (bukan musuh).
- **Napas & stamina** (M4): berlari memakai napas; memadamkan memakai tenaga;
  asap menurunkan napas bila terlalu lama di dalamnya.
- **Cuaca** (M4): kemarau/berangin/hujan deras — hujan menolong pemadaman tapi
  menyembunyikan jejak.
- **Inventory & crafting** (M5): air, pompa, sekat, benih; crafting alat
  pemadam sederhana dan barang investigasi (kertas arang, peta sketsa).
- **Investigasi** (M5–M8): papan bukti (V) merangkum status tiap insiden,
  barang bukti, dan skor kecurigaan warga; bukti didapat dari insiden yang
  dipadamkan, konfrontasi tersangka membuka babak berikutnya. Ending ditentukan
  jumlah bukti saat berkas diserahkan ke Mira (3 tingkat).
- **Warga & quest** (M7): quest kill berhadiah (Mira, Tomas), dialog konfrontasi
  per tersangka; pemulihan lahan lewat menanam benih di tanah bekas terbakar.
- **Save** (M3–M7): fog, tas, tunas, flag misi/insiden/ending sudah muat di
  skema v1; Save v2 hanya perlu bila kelak menyimpan kondisi tile per-tile.

## 4. Lima Pos Pantau (implementasi)

| # | Nama | Zona | Tersangka | Bukti Kunci |
|---|------|------|-----------|-------------|
| 1 | Pos Loji | greenwood_vale | Lena | Jerigen tanpa label |
| 2 | Pos Bukit | north_woods | Bram | Korek kuningan berkarat |
| 3 | Pos Lembah | ancient_ruins | Tomas | Nota pembelian tanah |
| 4 | Pos Jembatan | old_road | Sari | Sarung tangan berlumur oli |
| 5 | Pos Gerbang | greenwood_vale | Lena (pengakuan) | Surat sewa tanah |

Setiap pos: pemain memadamkan titik api (R), mendapat bukti, lalu mengonfrontasi
tersangkanya; rantai misi menuntun pos demi pos. Finale: serahkan seluruh berkas
ke **Mira** kapan saja setelah ada bukti — tingkat ending mengikuti persentase
bukti (Dalang Lolos <40%, Tertangkap 40–79%, Hutan Pulih ≥80%).

## 5. Tersangka (implementasi)

- **Lena** — Pos Loji & Pos Gerbang; menandatangani surat sewa karena tertekan,
  akhirnya membuka nama dalang.
- **Bram** — Pos Bukit; menolak menyewakan jalur sekat, petunjuk mengarah ke
  penyewa jasa.
- **Tomas** — Pos Lembah; saksi mesin di malam hari, nota tanah membongkar
  pemilik tungku arang.
- **Sari** — Pos Jembatan; diperintah lewat surat, upah benih impor.
- **Rangga Kelabu (dalang)** — pemilik tungku arang di balik semua surat;
  namanya terungkap menjelang finale, wujudnya sengaja tidak ditampilkan.

## 6. Daftar Bukti (implementasi)

| # | Bukti | Ditemukan | Berkaitan |
|---|-------|-----------|-----------|
| 1 | Jerigen minyak tanpa label | titik api Pos Loji | akses bahan bakar |
| 2 | Korek kuningan berkarat | titik api Pos Bukit | kebiasaan merokok Bram |
| 3 | Nota pembelian tanah | titik api Pos Lembah | motif ekspansi lahan |
| 4 | Sarung tangan oli | titik api Pos Jembatan | Sari diperintah lewat surat |
| 5 | Surat sewa tanah | titik api Pos Gerbang | identitas Rangga |

Barang bantu (bukan bukti): kain, tali, arang, pompa, sekat, benih, kertas
arang, peta sketsa (lihat `game/assets/data/items.json`).

## 7. Ekonomi & Kesulitan

- Sumber uang: peti harta, hadiah quest warga, reward misi/konfrontasi.
- Pengeluaran: potion di pasar (Esc).
- Kurva: Pos 1 (tutorial pocket: api kecil, angin kecil) → Pos 5 (api besar
  bertingkat, waktu siang lebih singkat karena asap menutup matahari).
- Target: 70% pemain menyelesaikan sekali; 30% kembali untuk ending terbaik.

## 8. Ending

1. **Dalang Lolos** (bukti < 40%): api padam, tapi pembakaran berlanjut.
2. **Tertangkap** (bukti 40–79%): Rangga tertangkap, penyewa desa lolos.
3. **Hutan Pulih** (bukti ≥ 80% + buku catatan tanah): semua terungkap;
   sistem pemulihan hutan penuh terbuka; epilog Greenwoods hijau kembali.

## 9. Roadmap Milestone

- **M1** (selesai): refactor sistem, dokumen ini, arsitektur, backend rapi,
  tes otomatis per sistem. (Gameplay belum berubah.)
- **M3** (selesai): dunia chunk (8 tile, aktivasi aktor), biome + musik per zona,
  fog of war dunia & peta mini, progres jelajah tersimpan di save.
- **M4** (selesai): api/asap gameplay (bahan bakar, kelembapan, angin, hujan,
  asap), napas (lari/semprot/asap), cuaca deterministik, HUD cuaca + hujan;
  grading/haze M2 menyatu di pipeline Fx.
- **M5** (selesai): inventory (tas ikut save), crafting data-driven
  (assets/data/items.json), rantai misi data-driven (assets/data/objectives.json),
  alat pemadaman (pompa/sekat), barang investigasi (kertas arang/peta sketsa),
  benih untuk tanah terbakar.
- **M6** (selesai): vertical slice Pos Loji — insiden kebakaran data-driven
  (assets/data/incidents.json), api → bukti (jerigen) → konfrontasi Lena,
  masuk rantai misi dan tersimpan lewat flag.
- **M7** (selesai): investigasi penuh — 3 insiden data-driven (Pos Loji,
  Pos Bukit/Bram/korek kuningan, Pos Lembah/Tomas/nota tanah), papan bukti (V)
  berisi status kasus/bukti/skor tersangka, konfrontasi tersangka data-driven,
  pemulihan tanah (tanam 3 benih → counter `restored`), resep benih, quest warga
  Tomas (4 kelelawar) di rantai misi.
- **M8** (selesai kode): Pos 4–5 (Pos Jembatan/Sari/sarung tangan oli,
  Pos Gerbang/Lena/surat sewa — mengungkap Rangga), finale 3 ending lewat
  Mira (bukti <40% Dalang Lolos, 40–79% Tertangkap, ≥80% Hutan Pulih) +
  overlay ending, layar judul + intro (sisa M2), rantai misi genap 18 tahap.
  Ekspor rilis: `godot --headless --path game --export-release "Linux" build/linux/arga.x86_64`
  (butuh template ekspor Godot 4.7.2 mono terpasang di mesin pemain).
