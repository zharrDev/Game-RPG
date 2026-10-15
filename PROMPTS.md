# 📜 PROMPTS.md — Character Bible & Prompt Library
## Proyek: Open World RPG Pixel Art (Medieval Fantasy · Side-view · Warrior)

_Dokumen 2 dari 3 · Semua prompt dalam bahasa Inggris (optimal untuk AI image generator)_

---

## 1. CHARACTER BIBLE — "Arga, Young Warrior Knight"

> Blok deskripsi ini WAJIB dipakai di setiap prompt supaya karakter konsisten di semua frame.

```
Young warrior knight, athletic young man, short spiky brown hair,
determined eyes, blue cloth tunic, silver metal shoulder pauldron on
the front shoulder, brown leather belt with small pouch, brown boots,
silver steel sword with simple crossguard.
```

### Palet warna (untuk cleanup manual di Aseprite/Piskel)

| Bagian | Warna utama | Shadow | Highlight |
|--------|-------------|--------|-----------|
| Skin | `#f2c9a0` | `#d1a276` | `#ffe4c4` |
| Rambut | `#7a4a26` | `#5a3419` | `#9c6534` |
| Tunic biru | `#4a6fa5` | `#35567d` | `#6b93c9` |
| Armor silver | `#b9c3cf` | `#7f8b98` | `#e2e8f0` |
| Kulit (leather) | `#8a5a30` | `#5e3d20` | `#a97845` |
| Pedang | `#dde4ec` | `#a8b4c2` | `#ffffff` |
| Outline | `#20202e` | — | — |
| Background (chroma key) | `#FF00FF` | — | — |

### STYLE BLOCK (tempel di akhir setiap prompt)

```
Classic 16-bit pixel art style, chunky clearly visible pixels, clean
readable silhouette, limited color palette with dark outline shading,
side view facing right, full body visible, flat solid pure magenta
#FF00FF background, no text, no watermark, no shadow, no ground.
```

### TEMPLATE prompt

```
Transform the attached character reference into [DESKRIPSI POSE/FRAME].
Keep the exact same character design, colors and proportions as the
reference: [CHARACTER BIBLE].
[STYLE BLOCK]
```

---

## 2. STATUS FRAME

| State | Frame | Status |
|-------|-------|--------|
| Idle | idle_1, idle_2 | ✅ Selesai |
| Walk | walk_1 … walk_4 | ✅ Selesai |
| Run | run_1, run_2, run_3 | ✅ Selesai |
| Run | run_4 | ⏳ Turn berikutnya |
| Jump | jump_1, jump_2, jump_3 | ⏳ Turn berikutnya |
| Attack | attack_1, attack_2, attack_3 | ⏳ Turn berikutnya |
| Skill | skill_1, skill_2, skill_3 | ⏳ Turn berikutnya |
| Hurt | hurt_1 | ⏳ Turn berikutnya |
| Death | death_1, death_2, death_3 | ⏳ Turn berikutnya |

File: `assets/character/frames/<nama>.png` · Referensi: `assets/character/reference/hero-reference.png`

---

## 3. PROMPT LENGKAP PER FRAME

> Cara pakai: attach `hero-reference.png` sebagai reference image (image-to-image), lalu paste prompt di bawah. Ganti hanya bagian deskripsi pose kalau mau variasi.

### IDLE (2 frame) — animasi diam/bernapas

**idle_1.png**
```
Transform the attached character reference into frame 1 of a 2-frame idle animation. Pose: standing relaxed breathing in, chest slightly expanded, arms relaxed at sides, sword tip resting near the ground, looking forward calmly. Keep the exact same character design, colors and proportions as the reference: young warrior knight, athletic young man, short spiky brown hair, determined eyes, blue cloth tunic, silver metal shoulder pauldron on the front shoulder, brown leather belt with small pouch, brown boots, silver steel sword with simple crossguard. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**idle_2.png**
```
Transform the attached character reference into frame 2 of a 2-frame idle animation. Pose: standing relaxed breathing out, body slightly lowered, head tilted down a few pixels, arms relaxed, sword tip resting near the ground. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

### WALK (4 frame) — siklus jalan

**walk_1.png** — contact, kaki kanan depan
```
Transform the attached character reference into frame 1 of a 4-frame walk cycle animation. Pose: contact pose, right leg stepped forward, left leg extended back, arms swinging naturally, sword held in hand at his side. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**walk_2.png** — passing, kaki kanan di bawah badan
```
Transform the attached character reference into frame 2 of a 4-frame walk cycle animation. Pose: passing pose, right leg directly under the body, left leg lifting off the ground, body slightly raised, arms mid-swing. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**walk_3.png** — contact mirror, kaki kiri depan
```
Transform the attached character reference into frame 3 of a 4-frame walk cycle animation. Pose: contact pose mirrored, left leg stepped forward, right leg extended back, arms swung to the opposite side, sword held in hand. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**walk_4.png** — passing, kaki kiri di bawah badan
```
Transform the attached character reference into frame 4 of a 4-frame walk cycle animation. Pose: passing pose, left leg directly under the body, right leg lifting off the ground, body slightly lowered, arms mid-swing opposite to frame 2. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

### RUN (4 frame) — siklus lari (eksaggerasi, badan condong)

**run_1.png**
```
Transform the attached character reference into frame 1 of a 4-frame run cycle animation. Pose: leaning forward with momentum, right leg extended far forward, left leg extended far back, arms pumping, sword trailing behind him. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**run_2.png**
```
Transform the attached character reference into frame 2 of a 4-frame run cycle animation. Pose: mid-stride, both legs passing under the body, body at its highest point, leaning forward, arms pumping. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**run_3.png**
```
Transform the attached character reference into frame 3 of a 4-frame run cycle animation. Pose: leaning forward with momentum, left leg extended far forward, right leg extended far back, arms pumping opposite to frame 1, sword trailing. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**run_4.png** ⏳
```
Transform the attached character reference into frame 4 of a 4-frame run cycle animation. Pose: mid-stride, both legs passing under the body again, body slightly lower than frame 2, leaning forward, arms pumping opposite. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

### JUMP (3 frame) — jongkok → naik → jatuh

**jump_1.png** ⏳ — crouch/antisipasi
```
Transform the attached character reference into frame 1 of a 3-frame jump animation: the crouch anticipation pose. Pose: knees deeply bent, body low and coiled close to the ground, sword pulled back ready to spring upward. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**jump_2.png** ⏳ — rising/naik
```
Transform the attached character reference into frame 2 of a 3-frame jump animation: the rising pose. Pose: legs tucked up beneath him, body stretched upward, sword raised pointing up, arms up for momentum. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**jump_3.png** ⏳ — falling/jatuh
```
Transform the attached character reference into frame 3 of a 3-frame jump animation: the falling pose. Pose: legs extended downward, arms out to the sides for balance, sword angled down, body leaning slightly forward. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

### ATTACK (3 frame) — kombo pedang

**attack_1.png** ⏳ — windup
```
Transform the attached character reference into frame 1 of a 3-frame sword attack combo animation: the windup. Pose: sword raised high above the head, body coiled back, ready to strike. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**attack_2.png** ⏳ — strike/slash
```
Transform the attached character reference into frame 2 of a 3-frame sword attack combo animation: the strike. Pose: sword slashing forward and down in a wide arc, body lunging forward at maximum extension, front leg stepping deep. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**attack_3.png** ⏳ — recovery
```
Transform the attached character reference into frame 3 of a 3-frame sword attack combo animation: the recovery. Pose: sword returned down at his side, body back to neutral stance with a slight forward lean. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

### SKILL (3 frame) — charge → cast → aftercast (skill sword aura)

**skill_1.png** ⏳ — charging
```
Transform the attached character reference into frame 1 of a 3-frame skill cast animation: channeling. Pose: both hands gripping the sword hilt, sword held vertical in front of him, glowing energy gathering around the blade, knees bent, concentrated expression. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**skill_2.png** ⏳ — release
```
Transform the attached character reference into frame 2 of a 3-frame skill cast animation: the release. Pose: sword swung up high, bright energy aura and slash burst bursting from the blade, body fully extended upward. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**skill_3.png** ⏳ — aftercast
```
Transform the attached character reference into frame 3 of a 3-frame skill cast animation: the aftercast. Pose: sword lowered, energy fading away, arms loose, slightly exhausted posture with bent knees. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

### HURT (1 frame) — reaksi kena hit

**hurt_1.png** ⏳
```
Transform the attached character reference into a hit reaction frame. Pose: body recoiling backward, head tilted back, arms flailing outward, mouth open in pain, one foot stumbling back. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

### DEATH (3 frame) — kolaps → jatuh → memudar

**death_1.png** ⏳
```
Transform the attached character reference into frame 1 of a 3-frame death animation: collapsing. Pose: knees buckling, body folding forward and downward, sword slipping from his hand. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**death_2.png** ⏳
```
Transform the attached character reference into frame 2 of a 3-frame death animation: fallen. Pose: lying on his back on the ground, arms spread out, sword lying beside him, eyes closed. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

**death_3.png** ⏳
```
Transform the attached character reference into frame 3 of a 3-frame death animation: faded. Pose: same fallen lying pose as the previous frame but the body is pale and semi-transparent fading away, small spirit wisps rising from the body, eyes closed. Keep the exact same character design, colors and proportions as the reference: [CHARACTER BIBLE]. Classic 16-bit pixel art style, chunky clearly visible pixels, clean readable silhouette, limited color palette with dark outline shading, side view facing right, full body visible, flat solid pure magenta #FF00FF background, no text, no watermark, no shadow, no ground.
```

---

## 4. TIPS PENGGUNAAN DI AI IMAGE GENERATOR LAIN

| Tool | Cara konsisten-kan karakter |
|------|------------------------------|
| **Midjourney** | `--cref <URL hero-reference.png> --cw 100` (character reference 100%) + `--ar 1:1 --niji 6 --style raw --no text, watermark, blurry, gradient` |
| **Leonardo.ai** | Image-to-Image dengan reference Arga, **Image Strength 0.35–0.5** (semakin rendah = makin bebas ganti pose) |
| **Stable Diffusion lokal** | img2img denoise **0.45–0.65** + **ControlNet OpenPose** (set pose tiap frame manual) + **LoRA karakter** (train dari 15–20 gambar Arga) → hasil paling konsisten |
| **SeaArt / OpenArt (SD online)** | IP-Adapter Reference-Only atau ControlNet Pose |
| **Bing / DALL·E / Ideogram** | Sebut "frame N of M walk cycle, same character", hasil paling tidak konsisten → butuh cleanup manual terbanyak |
| **Aseprite / Piskel / LibreSprite** | Bukan generator — dipakai untuk **rapikan** hasil AI: samakan grid, perbaiki jitter antar-frame, onion skin |

### Negative prompt (untuk SD / Leonardo)
```
text, watermark, signature, blurry, gradient background, photorealistic,
3d render, multiple characters, cropped body, extra limbs, deformed hands,
lowres, jpeg artifacts
```

### Rahasia konsistensi (urutan pentingnya)
1. **Character Bible identik** di setiap prompt (copy-paste, jangan diketik ulang)
2. **Image-to-image dari referensi**, jangan text-to-image murni
3. **Sebut nomor frame** ("frame 2 of 4") — model jadi paham ini bagian dari siklus
4. **Seed tetap** kalau tool-nya mendukung (Midjourney/SD)
5. **Cleanup manual** di Aseprite — ini WAJIB, bukan opsional: samakan tinggi badan, posisi kaki, dan palet antar frame

---

## 5. CATATAN PENTING SOAL HASIL AI

- AI image generator menghasilkan "pixel-art **style**" — belum benar-benar pixel-perfect dan antar frame masih ada jitter (goyangan kecil).
- Pakai hasil AI sebagai **base art**, lalu rapikan manual. Frame yang sudah rapi akan bikin animasi terasa profesional.
- Ukuran asli frame bisa diperkecil ke resolusi sprite sebenarnya, contoh:
  ```bash
  python3 tools/make_spritesheet.py --target-height 64
  ```
- Untuk 1 karakter lengkap butuh ±23 frame seperti daftar di atas — itu sudah cukup untuk 1 arah (menghadap kanan). Untuk menghadap kiri, cukup **mirror/flip horizontal** di engine.
