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
5. **Pulihkan** hutan: tanam tunas, bersihkan arang, ganti sekat yang rusak.
6. **Kunci bukti** di Pos ke X → investigasi naik tingkat → pos berikutnya.

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
- **Investigasi** (M5–M8): papan bukti di Pos; bukti punya asal, kondisi rusak,
  dan tingkat kepercayaan. Kesimpulan salah mengarah ke ending yang lebih buruk.
- **Warga & quest** (M7): jadwal harian, dialog bercabang; reputasi desa
  memengaruhi akses bukti dan bantuan saat pemadaman besar.
- **Save v2** (M8): state dunia (tile terbakar/pulih), inventori, papan bukti —
  dimigrasi dari v1 tanpa kehilangan profil lama.

## 4. Lima Pos Pantau

| # | Nama | Biome | Kunci | Boss/Bukti Kunci |
|---|------|-------|-------|------------------|
| 1 | Pos Loji | pinggir desa | padam 3 titik, pompa pertama | tukang arang; jerigen tak bertuan |
| 2 | Pos Gambut | rawa kering | api gambut tak terlihat | kebakaran bawah tanah; arang asin berkode |
| 3 | Pos Bukit | punggung bukit | angin kencang | jalur sekat rusak; jejak sepatu kerja |
| 4 | Pos Lembah | lembah sempit | api tepi sungai | warga menyembunyikan sesuatu; map hotspot |
| 5 | Pos Inti | hutan dalam | pemadaman besar + finale | panggung dalang; penentuan ending |

Setiap pos: peta lebih tajam (fog makin kecil), API lebih ganas, tersangka baru,
dan bukti yang saling mengunci. Pos 5 adalah panggung **finale**: pemain harus
**memilih** mana bukti yang dibawa saat konfrontasi.

## 5. Tersangka & Red Herring

- **Pak Wira** — juru arang. Punggung dekat ke semua titik kebakaran; tapi
  tungku arangnya legal dan ia justru rugi bila hutan habis. Red herring awal.
- **Bu Sari** — pemilik warung. Membeli kayu bakar murah dari siapa pun;
  panik saat diaudit. Peluang uang, bukan pembakar.
- **Jagas** — pemburu muda, mudah marah, pernah dipenjara. Banyak jejak
  sepatu kebetulan mirip. Bisa jadi sekutu atau gagal paham.
- **Rangga Kelabu (dalang)** — bukan warga. Nama samaran penyewa jasa: aktor
  intelektual di balik ekspansi lahan, pembakar profesional yang memanfaatkan
  kemarau. Penyewa sebenarnya adalah seorang figur desa yang selama ini
  "membantu" pemadaman (diungkap dari bukti yang saling mengunci di Pos 4–5).

## 6. Daftar Bukti (klon; diperluas saat produksi)

| # | Bukti | Ditemukan | Berkaitan |
|---|-------|-----------|-----------|
| 1 | Jerigen minyak tanpa label | titik api Pos 1 | siapa pun dengan akses pasar |
| 2 | Nota pembelian tanah | peti di Pos 1 | ekspansi lahan (motive) |
| 3 | Korek kuningan berkarat | semak Pos 1 | kebiasaan merokok (Jagas/Pak Wira) |
| 4 | Jejak sepatu kerja | jalur sekat Pos 2 | ukuran 42, dua orang |
| 5 | Arang asin (garam laut) | rawa Pos 2 | siapa yang punya akses garam |
| 6 | Peta sketsa hotspot | tenda Pos 2 | pola pembakaran terencana |
| 7 | Sekat rusak sengaja | bukit Pos 3 | arah angin diatur |
| 8 | Kesaksian Tomas | pasar | suara mesin di malam hari |
| 9 | Surat ancaman ke Bu Sari | warung | tekanan ekonomi |
| 10 | Slip bayaran "Rangga" | pos inti | identitas dalang |
| 11 | Kantong garam tua | rumah terkunci Pos 4 | mengunci tersangka |
| 12 | Buku catatan tanah | brankas finale | ending terbaik |

## 7. Ekonomi & Kesulitan

- Sumber uang: menjual arang bersih, menyelamatkan properti, hadiah warga.
- Pengeluaran: alat pemadam, benih tunas, benang sekat, perbaikan pompa.
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
- **M5**: inventory, crafting, objective/misi data-driven.
- **M6**: vertical slice Pos Loji end-to-end (padam → bukti → tersangka).
- **M7**: investigasi penuh + papan bukti + building + quest warga.
- **M8**: Pos 2–5, finale, Save v2, cuaca penuh, polish, ekspor
  (sisa M2: layar judul & intro).
