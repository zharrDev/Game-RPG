/**
 * api.test.mjs — tes kontrak HTTP terhadap backend C# (RpgServer) yang sedang jalan.
 * Dijalankan oleh scripts/test-all.sh dengan env RPG_BASE_URL.
 *
 * Frontend game adalah aplikasi Godot (./game), jadi server ini harus murni API:
 * tidak ada HTML, tidak ada halaman statis. Tes "frontend" di bawah menjaga itu.
 */

import { test } from "node:test";
import assert from "node:assert/strict";
import { writeFile } from "node:fs/promises";

const BASE = process.env.RPG_BASE_URL ?? "http://127.0.0.1:5099";
const markerFile = () => process.env.RPG_MARKER_FILE ?? "/tmp/rpg-marker.json";

async function call(path, options = {}) {
  const response = await fetch(BASE + path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  const text = await response.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    body = text;
  }
  return { status: response.status, body, contentType: response.headers.get("content-type") };
}

/** Payload save sesuai kontrak yang dinormalkan server (lihat StateValidator.cs). */
function createState(zone = "greenwood", checkpoint = "camp_start") {
  return {
    player: {
      x: 0, y: 0, facing: 1, hp: 100, maxHp: 100,
      level: 1, xp: 0, skillLevel: 0, skills: [],
    },
    world: { zone, checkpoint, flags: {}, defeated: {} },
    stats: { playtime: 0, deaths: 0, kills: 0 },
  };
}

function maxHpFor(level) {
  return 100 + (level - 1) * 15;
}

function validState(overrides = {}) {
  const state = createState();
  Object.assign(state.player, overrides.player ?? {});
  Object.assign(state.world, overrides.world ?? {});
  Object.assign(state.stats, overrides.stats ?? {});
  state.stats.playtime = overrides.playtime ?? 42;
  return state;
}

const suffix = Date.now().toString(36);
let profileId = null;

test("health: server siap dan mengaku sebagai RpgServer", async () => {
  const { status, body } = await call("/api/health");
  assert.equal(status, 200);
  assert.equal(body.status, "ok");
  assert.equal(body.character, "arga");
  assert.equal(body.slots, 3);
  assert.ok(body.database.endsWith(".db"), "melaporkan lokasi database sqlite");
});

test("frontend: server hanya API — tidak ada HTML/halaman web", async () => {
  // "/" menjelaskan letak frontend (aplikasi Godot), dalam JSON.
  const root = await call("/");
  assert.equal(root.status, 200);
  assert.match(root.contentType ?? "", /application\/json/);
  assert.equal(root.body.engine, "godot", "frontend = game Godot");
  assert.match(root.body.run ?? "", /scripts\/dev\.sh/, "menjelaskan cara menjalankan game");
  assert.doesNotMatch(JSON.stringify(root.body), /<html/i);

  // sisa-sisa frontend HTML tidak boleh ada lagi
  for (const path of ["/index.html", "/prototype.html", "/style.css", "/js/game.js"]) {
    const response = await call(path);
    assert.equal(response.status, 404, `${path} tidak disajikan`);
  }

  // aset game tetap bisa diambil (dipakai untuk memeriksa hasil generator)
  const world = await call("/assets/world/overworld.json");
  assert.equal(world.status, 200);
  assert.equal(world.body.width, 96);
  const sheet = await fetch(`${BASE}/assets/character/hero_atlas.png`);
  assert.equal(sheet.status, 200);
  assert.match(sheet.headers.get("content-type") ?? "", /image\/png/);
  const bytes = Buffer.from(await sheet.arrayBuffer());
  assert.equal(bytes.subarray(1, 4).toString(), "PNG", "atlas hero benar-benar PNG");

  // pratinjau PNG (docs/) boleh dibuka di browser untuk meninjau seni
  const preview = await fetch(`${BASE}/preview/preview-world.png`);
  assert.equal(preview.status, 200);
  assert.match(preview.headers.get("content-type") ?? "", /image\/png/);
});

test("profil: buat, validasi nama, daftar, dan ambil detail", async () => {
  const invalid = await call("/api/profiles", { method: "POST", body: JSON.stringify({ name: "  " }) });
  assert.equal(invalid.status, 400);
  assert.match(invalid.body.error, /nama profil/);

  const created = await call("/api/profiles", { method: "POST", body: JSON.stringify({ name: `Arga ${suffix}` }) });
  assert.equal(created.status, 201);
  profileId = created.body.profile.id;
  assert.equal(typeof profileId, "string");
  assert.equal(profileId.length, 12);

  const list = await call("/api/profiles");
  assert.equal(list.status, 200);
  assert.ok(list.body.profiles.some((p) => p.id === profileId));

  const detail = await call(`/api/profiles/${profileId}`);
  assert.equal(detail.status, 200);
  assert.equal(detail.body.profile.level, 1);
  assert.equal(detail.body.slots.length, 0);

  const missing = await call("/api/profiles/tidakada12345");
  assert.equal(missing.status, 404);
});

test("save slot: payload dari klien disimpan, dinormalkan, dan dibaca ulang utuh", async () => {
  const state = validState({
    player: { level: 4, xp: 55, hp: 90, maxHp: maxHpFor(4), skills: ["aura_slash"], skillLevel: 1, x: 1234.5, facing: -1 },
  });
  const saved = await call(`/api/profiles/${profileId}/slots/1`, { method: "PUT", body: JSON.stringify(state) });
  assert.equal(saved.status, 200);
  assert.equal(saved.body.saved, true);
  assert.equal(saved.body.slot, 1);
  assert.equal(saved.body.info.level, 4);

  const loaded = await call(`/api/profiles/${profileId}/slots/1`);
  assert.equal(loaded.status, 200);
  assert.equal(loaded.body.state.player.level, 4);
  assert.equal(loaded.body.state.player.hp, 90);
  assert.equal(loaded.body.state.player.x, 1234.5);
  assert.equal(loaded.body.state.player.facing, -1);
  assert.deepEqual(loaded.body.state.player.skills, ["aura_slash"]);
  assert.equal(loaded.body.state.world.zone, "greenwood");
  assert.equal(loaded.body.state.version, "2");
  assert.equal(loaded.body.state.world.day, 1, "save v1 dimigrasikan ke hari 1");
  assert.equal(loaded.body.state.world.villageSafety, 100);
  assert.equal(loaded.body.state.world.forestHealth, 100);
  assert.ok(loaded.body.state.savedAt, "server menstempel waktu simpan");

  const resume = await call(`/api/profiles/${profileId}/resume`);
  assert.equal(resume.status, 200);
  assert.equal(resume.body.slot, 1);
  assert.equal(resume.body.info.checkpoint, "camp_start");

  const profile = await call(`/api/profiles/${profileId}`);
  assert.equal(profile.body.profile.level, 4, "ringkasan profil ikut ter-update");
  assert.equal(profile.body.profile.zone, "greenwood");
});

test("save slot: nilai ekstrem di-clamp server, JSON rusak ditolak", async () => {
  const wild = validState({
    player: { level: 500, xp: -999, hp: 99999, maxHp: 50, skillLevel: 99, x: 1e9, gold: 99999999, potions: 500 },
    world: { checkpoint: "camp_ruins", zone: "ruins" },
  });
  const saved = await call(`/api/profiles/${profileId}/slots/2`, { method: "PUT", body: JSON.stringify(wild) });
  assert.equal(saved.status, 200);
  const loaded = await call(`/api/profiles/${profileId}/slots/2`);
  assert.equal(loaded.body.state.player.level, 99);
  assert.equal(loaded.body.state.player.hp, 50, "hp dibatasi maxHp");
  assert.equal(loaded.body.state.player.xp, 0);
  assert.equal(loaded.body.state.player.skillLevel, 3, "skillLevel dibatasi 3");
  assert.equal(loaded.body.state.player.x, 1000000, "x dibatasi batas atas");
  assert.equal(loaded.body.state.player.gold, 9999999, "gold dibatasi batas atas");
  assert.equal(loaded.body.state.player.potions, 99, "potions dibatasi batas atas");
  assert.equal(loaded.body.state.world.zone, "ruins");

  const badSkill = await call(`/api/profiles/${profileId}/slots/2`, {
    method: "PUT",
    body: JSON.stringify({ player: { skills: ["super_cheat"] } }),
  });
  assert.equal(badSkill.status, 400);
  assert.match(badSkill.body.error, /skill tidak dikenal/);

  const notObject = await call(`/api/profiles/${profileId}/slots/2`, { method: "PUT", body: JSON.stringify("bukan state") });
  assert.equal(notObject.status, 400);

  const brokenJson = await call(`/api/profiles/${profileId}/slots/2`, { method: "PUT", body: "{rusak" });
  assert.equal(brokenJson.status, 400);

  const badSlot = await call(`/api/profiles/${profileId}/slots/9`, { method: "PUT", body: JSON.stringify(validState()) });
  assert.equal(badSlot.status, 400);
  assert.match(badSlot.body.error, /slot harus 1-3/);

  const ghostProfile = await call("/api/profiles/tidakada12345/slots/1", { method: "PUT", body: JSON.stringify(validState()) });
  assert.equal(ghostProfile.status, 404);
});

test("save v2: hari & meter bertahan, nilai liar dijepit", async () => {
  const seasonal = validState({ world: { day: 7, villageSafety: 42.5, forestHealth: 88 } });
  const saved = await call(`/api/profiles/${profileId}/slots/3`, { method: "PUT", body: JSON.stringify(seasonal) });
  assert.equal(saved.status, 200);
  const loaded = await call(`/api/profiles/${profileId}/slots/3`);
  assert.equal(loaded.body.state.version, "2");
  assert.equal(loaded.body.state.world.day, 7);
  assert.equal(loaded.body.state.world.villageSafety, 42.5);
  assert.equal(loaded.body.state.world.forestHealth, 88);

  const wild = validState({ world: { day: -3, villageSafety: 150, forestHealth: -40 } });
  await call(`/api/profiles/${profileId}/slots/3`, { method: "PUT", body: JSON.stringify(wild) });
  const clamped = await call(`/api/profiles/${profileId}/slots/3`);
  assert.equal(clamped.body.state.world.day, 1);
  assert.equal(clamped.body.state.world.villageSafety, 100);
  assert.equal(clamped.body.state.world.forestHealth, 0);

  // bersihkan slot 3: tes berikutnya menguji slot kosong
  const cleared = await call(`/api/profiles/${profileId}/slots/3`, { method: "DELETE" });
  assert.equal(cleared.status, 200);
});

test("journey: perubahan progres tercatat otomatis (level, checkpoint, zone, boss)", async () => {
  const before = await call(`/api/profiles/${profileId}/journey`);
  const baseline = before.body.events.length;

  const level2 = validState({ player: { level: 2, xp: 10 }, world: { checkpoint: "camp_start" } });
  await call(`/api/profiles/${profileId}/slots/1`, { method: "PUT", body: JSON.stringify(level2) });

  const bossState = validState({
    player: { level: 6, xp: 20 },
    world: { checkpoint: "camp_ruins", zone: "ruins", flags: { boss_warden_defeated: true } },
  });
  const saved = await call(`/api/profiles/${profileId}/slots/1`, { method: "PUT", body: JSON.stringify(bossState) });
  assert.equal(saved.status, 200);
  const kinds = saved.body.events.map((e) => e.kind);
  assert.ok(kinds.includes("level_up"), `event level_up tercatat (${kinds})`);
  assert.ok(kinds.includes("checkpoint"), "event checkpoint tercatat");
  assert.ok(kinds.includes("zone"), "event pindah zona tercatat");
  assert.ok(kinds.includes("flag"), "event boss/flag tercatat");

  const journey = await call(`/api/profiles/${profileId}/journey`);
  assert.ok(journey.body.events.length > baseline);
  assert.equal(journey.body.profile.bosses, 1, "jumlah boss dihitung dari flag");
  assert.equal(journey.body.profile.zone, "ruins");

  const manual = await call(`/api/profiles/${profileId}/events`, {
    method: "POST",
    body: JSON.stringify({ kind: "catatan", detail: "mulai dari test" }),
  });
  assert.equal(manual.status, 200);
  assert.equal(manual.body.events[0].kind, "catatan");
});

test("slot kosong: GET/DELETE mengembalikan 404", async () => {
  const empty = await call(`/api/profiles/${profileId}/slots/3`);
  assert.equal(empty.status, 404);
  const deleteEmpty = await call(`/api/profiles/${profileId}/slots/3`, { method: "DELETE" });
  assert.equal(deleteEmpty.status, 404);
});

test("simulasi klien game: state hasil main sungguhan diterima backend", async () => {
  // bentuk payload sama dengan yang dikirim klien Godot (BuildState -> SaveApi)
  const payload = validState({
    player: { level: 5, maxHp: maxHpFor(5), hp: 77, x: 300, facing: -1, skills: ["aura_slash"], skillLevel: 3, gold: 145, potions: 4 },
    world: { zone: "old_road", checkpoint: "bridge_camp", flags: { boss_warden_defeated: true } },
    stats: { kills: 9, deaths: 2 },
    playtime: 321.5,
  });

  const saved = await call(`/api/profiles/${profileId}/slots/2`, { method: "PUT", body: JSON.stringify(payload) });
  assert.equal(saved.status, 200, JSON.stringify(saved.body));
  const loaded = await call(`/api/profiles/${profileId}/slots/2`);
  assert.equal(loaded.body.state.world.zone, "old_road");
  assert.equal(loaded.body.state.world.checkpoint, "bridge_camp");
  assert.equal(loaded.body.state.player.hp, 77);
  assert.equal(loaded.body.state.player.facing, -1);
  assert.equal(loaded.body.state.stats.kills, 9);
  assert.equal(loaded.body.state.stats.deaths, 2);
  assert.equal(loaded.body.state.stats.playtime, 321.5);
  assert.equal(loaded.body.state.player.level, 5);
  assert.equal(loaded.body.state.player.gold, 145, "gold dari peti sampai ke server");
  assert.equal(loaded.body.state.player.potions, 4, "stok potion tersimpan");
});

test("hapus: slot dan profil benar-benar hilang dari database", async () => {
  const del = await call(`/api/profiles/${profileId}/slots/1`, { method: "DELETE" });
  assert.equal(del.status, 200);
  const gone = await call(`/api/profiles/${profileId}/slots/1`);
  assert.equal(gone.status, 404);

  const deleteProfile = await call(`/api/profiles/${profileId}`, { method: "DELETE" });
  assert.equal(deleteProfile.status, 200);
  const check = await call(`/api/profiles/${profileId}`);
  assert.equal(check.status, 404);
});

// ---- penanda untuk tes persistensi (server direstart setelah file ini selesai) ----
test("siapkan penanda persistensi untuk fase berikutnya", async () => {
  const markerName = `PersistMarker-${suffix}`;
  const created = await call("/api/profiles", { method: "POST", body: JSON.stringify({ name: markerName }) });
  assert.equal(created.status, 201);
  const id = created.body.profile.id;
  const payload = validState({
    player: { level: 7, xp: 33, hp: 111, maxHp: maxHpFor(7), x: 777.25 },
    world: { checkpoint: "camp_ruins", zone: "ruins", flags: { boss_warden_defeated: true } },
    playtime: 555,
  });
  const saved = await call(`/api/profiles/${id}/slots/3`, { method: "PUT", body: JSON.stringify(payload) });
  assert.equal(saved.status, 200);

  const marker = await fetch(`${BASE}/api/profiles`);
  const found = (await marker.json()).profiles.find((p) => p.name === markerName);
  assert.ok(found, "profil penanda tampil di daftar");
  await writeFile(markerFile(), JSON.stringify({ id: found.id, name: markerName, slot: 3 }), "utf8");
  console.log(`# marker profil ditulis ke ${markerFile()}: ${found.id}`);
});
