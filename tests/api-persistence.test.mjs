/**
 * api-persistence.test.mjs — fase 2: dijalankan SETELAH server C# direstart.
 * Membuktikan progres benar-benar tersimpan di SQLite, bukan cuma di memori.
 */

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const BASE = process.env.RPG_BASE_URL ?? "http://127.0.0.1:5099";
const markerFile = () => process.env.RPG_MARKER_FILE ?? "/tmp/rpg-marker.json";

async function call(path) {
  const response = await fetch(BASE + path);
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

test("persistensi: profil, slot, dan catatan perjalanan tetap ada setelah server restart", async () => {
  let marker;
  try {
    marker = JSON.parse(await readFile(markerFile(), "utf8"));
  } catch {
    assert.fail(`marker tidak ditemukan (${markerFile()}) — jalankan api.test.mjs lebih dulu`);
  }

  const health = await call("/api/health");
  assert.equal(health.status, 200, "server hidup lagi setelah restart");

  const profile = await call(`/api/profiles/${marker.id}`);
  assert.equal(profile.status, 200, "profil penanda masih ada");
  assert.equal(profile.body.profile.name, marker.name);
  assert.equal(profile.body.profile.level, 7, "level tersimpan");
  assert.equal(profile.body.profile.zone, "ruins", "zona tersimpan");
  assert.equal(profile.body.profile.checkpoint, "camp_ruins", "checkpoint tersimpan");
  assert.equal(profile.body.profile.bosses, 1, "flag boss tersimpan");
  assert.equal(Math.round(profile.body.profile.playtime), 555, "playtime tersimpan");

  const slot = await call(`/api/profiles/${marker.id}/slots/${marker.slot}`);
  assert.equal(slot.status, 200, "slot save masih ada");
  assert.equal(slot.body.state.player.level, 7);
  assert.equal(slot.body.state.player.hp, 111);
  assert.equal(slot.body.state.player.x, 777.25);
  assert.equal(slot.body.state.world.flags.boss_warden_defeated, true);

  const events = await call(`/api/profiles/${marker.id}/journey`);
  assert.ok(events.body.events.length > 0, "jejak perjalanan tersimpan");
  assert.ok(
    events.body.events.some((e) => e.kind === "profile_created"),
    "event pembuatan profil ikut tersimpan",
  );

  // lanjutkan perjalanan: resume harus mengembalikan slot terakhir
  const resume = await call(`/api/profiles/${marker.id}/resume`);
  assert.equal(resume.status, 200);
  assert.equal(resume.body.slot, marker.slot);
  assert.equal(resume.body.state.world.zone, "ruins");
});
