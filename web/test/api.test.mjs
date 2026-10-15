/**
 * api.test.mjs — tes kontrak HTTP terhadap backend C# (RpgServer) yang sedang jalan.
 * Dijalankan oleh scripts/test-all.sh dengan env RPG_BASE_URL.
 */

import { test } from "node:test";
import assert from "node:assert/strict";
import { writeFile } from "node:fs/promises";
import { createRuntime, createState, maxHpFor, serializeState, snapshot, travelTo } from "../js/core.js";

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

const suffix = Date.now().toString(36);
let profileId = null;

function validState(overrides = {}) {
  const state = createState("x", "Tester", "greenwood");
  Object.assign(state.player, overrides.player ?? {});
  Object.assign(state.world, overrides.world ?? {});
  return serializeState(state, overrides.playtime ?? 42);
}

test("health: server siap dan mengaku sebagai RpgServer", async () => {
  const { status, body } = await call("/api/health");
  assert.equal(status, 200);
  assert.equal(body.status, "ok");
  assert.equal(body.character, "arga");
  assert.equal(body.slots, 3);
  assert.ok(body.database.endsWith(".db"), "melaporkan lokasi database sqlite");
});

test("static: halaman game, modul js, dan spritesheet disajikan server", async () => {
  const page = await call("/");
  assert.equal(page.status, 200);
  assert.match(page.contentType ?? "", /text\/html/);
  assert.match(page.body, /id="game"/, "canvas game tersedia");
  assert.match(page.body, /js\/game\.js/, "modul game dirujuk");

  const core = await call("/js/core.js");
  assert.equal(core.status, 200);

  const atlas = await call("/assets/character/build/arga_spritesheet.json");
  assert.equal(atlas.status, 200);
  assert.equal(atlas.body.animations.length, 8, "8 animasi di atlas");
  assert.equal(atlas.body.frames.length, 23, "23 frame di atlas");

  const sheet = await fetch(`${BASE}/assets/character/build/arga_spritesheet.png`);
  assert.equal(sheet.status, 200);
  assert.match(sheet.headers.get("content-type") ?? "", /image\/png/);
  const bytes = Buffer.from(await sheet.arrayBuffer());
  assert.equal(bytes.subarray(1, 4).toString(), "PNG", "file spritesheet benar-benar PNG");
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
  const state = validState({ player: { level: 4, xp: 55, hp: 90, maxHp: maxHpFor(4), skills: ["aura_slash"], skillLevel: 1, x: 1234.5, facing: -1 } });
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
  assert.equal(loaded.body.state.version, "1");
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
    player: { level: 500, xp: -999, hp: 99999, maxHp: 50, skillLevel: 99, x: 1e9 },
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

test("simulasi klien→server: payload hasil main sungguhan diterima backend", async () => {
  const rt = createRuntime(createState("x", "Tester", "greenwood"));
  travelTo(rt, "old_road");
  rt.player.x = 300;
  rt.player.hp = 77;
  rt.state.player.level = 5;
  rt.state.stats.kills = 9;
  rt.state.stats.deaths = 2;
  const payload = snapshot(rt);
  assert.equal(payload.world.zone, "old_road");

  const saved = await call(`/api/profiles/${profileId}/slots/2`, { method: "PUT", body: JSON.stringify(payload) });
  assert.equal(saved.status, 200, JSON.stringify(saved.body));
  const loaded = await call(`/api/profiles/${profileId}/slots/2`);
  assert.equal(loaded.body.state.world.zone, "old_road");
  assert.equal(loaded.body.state.player.hp, 77);
  assert.equal(loaded.body.state.stats.kills, 9);
  assert.equal(loaded.body.state.stats.deaths, 2);
  assert.equal(loaded.body.state.player.level, 5);
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
