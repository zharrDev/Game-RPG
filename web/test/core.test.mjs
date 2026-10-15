import { test } from "node:test";
import assert from "node:assert/strict";
import {
  COMBAT,
  PHYSICS,
  createInput,
  createRuntime,
  createState,
  grantXp,
  maxHpFor,
  normalizeState,
  progressInfo,
  serializeState,
  simulate,
  snapshot,
  spawnEnemies,
  travelTo,
  unlockedSkills,
  xpNeeded,
  zoneById,
} from "../js/core.js";

const DT = 1 / 60;

function sim(rt, seconds, held = {}) {
  const input = { ...createInput(), ...held };
  const events = [];
  for (let i = 0; i < Math.round(seconds / DT); i += 1) {
    events.push(...simulate(rt, input, DT));
  }
  return events;
}

function freshState(overrides = {}) {
  const state = createState("test-profile", "Tester", "greenwood");
  Object.assign(state.player, overrides.player ?? {});
  Object.assign(state.world, overrides.world ?? {});
  return state;
}

test("createState: mulai dari checkpoint Greenvwood dengan level 1", () => {
  const state = createState("p1", "Arga", "greenwood");
  const zone = zoneById("greenwood");
  assert.equal(state.player.level, 1);
  assert.equal(state.player.hp, maxHpFor(1));
  assert.equal(state.player.x, zone.checkpoint.x);
  assert.equal(state.player.y, zone.groundY);
  assert.equal(state.world.zone, "greenwood");
  assert.deepEqual(state.player.skills, []);
});

test("gerak maju & mundur: kecepatan jalan sesuai PHYSICS tanpa keluar zona", () => {
  const rt = createRuntime(freshState());
  const startX = rt.player.x;
  sim(rt, 1, { right: true });
  const forward = rt.player.x - startX;
  assert.ok(Math.abs(forward - PHYSICS.walkSpeed) < 6, `maju ~${PHYSICS.walkSpeed}px, dapat ${forward.toFixed(1)}`);
  assert.equal(rt.player.facing, 1);

  sim(rt, 0.5, { left: true });
  assert.equal(rt.player.facing, -1);
  assert.ok(rt.player.x < startX + forward, "posisi mundur setelah menekan kiri");

  sim(rt, 30, { left: true });
  assert.ok(rt.player.x >= 12, "tidak menembus batas kiri zona");
});

test("berlari: lebih cepat dari jalan dan animasi run aktif", () => {
  const walkRt = createRuntime(freshState());
  sim(walkRt, 1, { right: true });
  const runRt = createRuntime(freshState());
  sim(runRt, 1, { right: true, run: true });
  assert.ok(runRt.player.x > walkRt.player.x + 60, "run menempuh jarak lebih jauh");
  assert.equal(runRt.player.anim, "run");
  assert.equal(walkRt.player.anim, "walk");
  assert.ok(Math.abs(runRt.player.x - 140 - PHYSICS.runSpeed) < 8);
});

test("idle saat diam dan animasi berhenti di tanah", () => {
  const rt = createRuntime(freshState());
  sim(rt, 0.5);
  assert.equal(rt.player.anim, "idle");
  assert.equal(rt.player.onGround, true);
});

test("lompat: ada takeoff (jump_1), naik (jump_up), jatuh (jump_down), lalu mendarat", () => {
  const rt = createRuntime(freshState());
  const groundY = rt.zone.groundY;
  const events = sim(rt, 0.06, { jump: true });
  assert.equal(rt.player.anim, "jump_takeoff", "pose crouch sebelum meluncur");
  void events;

  let minY = groundY;
  let sawUp = false;
  let sawDown = false;
  let jumpEvent = false;
  const still = inputWith({ jump: false });
  for (let i = 0; i < 180; i += 1) {
    const events = simulate(rt, still, DT);
    if (events.some((e) => e.type === "jump")) jumpEvent = true;
    minY = Math.min(minY, rt.player.y);
    if (rt.player.anim === "jump_up") sawUp = true;
    if (rt.player.anim === "jump_down") sawDown = true;
  }
  assert.ok(jumpEvent, "event jump dipancarkan");
  assert.ok(minY < groundY - 40, `melompat naik (minY ${minY.toFixed(1)} vs tanah ${groundY})`);
  assert.ok(sawUp, "frame naik terpakai");
  assert.ok(sawDown, "frame jatuh terpakai");
  sim(rt, 1.2);
  assert.equal(rt.player.onGround, true);
  assert.equal(rt.player.y, groundY);
});

function inputWith(values) {
  return { ...createInput(), ...values };
}

test("serangan: memukul slime sampai mati, dapat XP dan kill tercatat", () => {
  const rt = createRuntime(freshState());
  const slime = rt.enemies.find((e) => e.type === "slime");
  rt.player.x = slime.x - 26;
  rt.player.y = rt.zone.groundY;
  const events = sim(rt, 1.6, { attack: true });
  assert.ok(events.some((e) => e.type === "hit"), "hit terdeteksi");
  assert.ok(slime.hp <= 0, `slime mati (hp ${slime.hp})`);
  assert.ok(rt.state.stats.kills >= 1);
  assert.ok(rt.state.player.xp >= 18, "XP bertambah");
  assert.equal(rt.player.anim === "attack" || rt.player.anim === "idle" || rt.player.anim === "hurt", true);
});

test("satu serangan tidak menghitung damage berulang tiap frame", () => {
  const rt = createRuntime(freshState());
  const slime = rt.enemies.find((e) => e.type === "slime");
  rt.player.x = slime.x - 26;
  const events = sim(rt, COMBAT.attackDuration, { attack: true });
  const hits = events.filter((e) => e.type === "hit" && e.enemy === slime.id);
  assert.equal(hits.length, 1, `satu attack = satu hit, dapat ${hits.length}`);
});

test("progresi: XP menaikkan level, HP maksimum, dan membuka skill di level 3", () => {
  const state = freshState();
  const levels = grantXp(state, xpNeeded(1) + 10);
  assert.deepEqual(levels, [2]);
  assert.equal(state.player.level, 2);
  assert.equal(state.player.maxHp, maxHpFor(2));
  assert.equal(state.player.xp, 10);
  assert.deepEqual(unlockedSkills(2), []);

  grantXp(state, 5000);
  assert.ok(state.player.level >= 3, `level ${state.player.level} >= 3`);
  assert.deepEqual(state.player.skills, ["aura_slash"]);
  assert.ok(state.player.skillLevel >= 1);
});

test("skill: menguras energi, menyerang semua musuh dalam radius, dan menahan saat energi habis", () => {
  const rt = createRuntime(freshState({ player: { level: 3, maxHp: maxHpFor(3), hp: maxHpFor(3) } }));
  const slime = rt.enemies.find((e) => e.type === "slime");
  rt.player.x = slime.x - 40;
  const events = sim(rt, 0.1, { skill: true });
  assert.ok(events.some((e) => e.type === "skill"), "skill aktif");
  assert.ok(slime.hp < 30, `slime terkena aura (hp ${slime.hp})`);
  assert.ok(
    rt.player.energy >= 100 - COMBAT.skillCost && rt.player.energy < 100 - COMBAT.skillCost + 2,
    `energi terpotong ${COMBAT.skillCost} lalu regen sedikit (dapat ${rt.player.energy.toFixed(1)})`,
  );

  rt.player.energy = 0;
  rt.player.skillTimer = 0;
  rt.player.animLocked = false; // skillTimer dipangkas manual di tes, jadi buka kuncinya sendiri
  const blocked = sim(rt, 0.1, { skill: true });
  assert.ok(blocked.some((e) => e.type === "skill_no_energy"), "skill diblokir saat energi habis");
});

test("skill terkunci sebelum level 3", () => {
  const rt = createRuntime(freshState());
  const events = sim(rt, 0.2, { skill: true });
  assert.ok(!events.some((e) => e.type === "skill"));
});

test("musuh: kontak mengurangi HP, memberi invuln, lalu tumbang & respawn di checkpoint", () => {
  const rt = createRuntime(freshState());
  const slime = rt.enemies.find((e) => e.type === "slime");
  rt.player.x = slime.x;
  rt.player.energy = 100;
  const events = sim(rt, 0.1);
  assert.ok(events.some((e) => e.type === "player_hurt"), "kena serangan musuh");
  assert.ok(rt.player.hp < rt.player.maxHp);
  assert.ok(rt.player.invuln > 0, "invulnerability aktif");
  assert.equal(rt.player.anim, "hurt");

  rt.player.hp = 5;
  rt.player.invuln = 0;
  const deathEvents = sim(rt, 3.5);
  assert.ok(deathEvents.some((e) => e.type === "player_died"), "pemain tumbang");
  const respawn = deathEvents.find((e) => e.type === "respawn");
  assert.ok(respawn, "respawn terjadi");
  assert.equal(respawn.x, rt.zone.checkpoint.x);
  assert.equal(rt.state.stats.deaths, 1);
  assert.equal(rt.player.hp, rt.player.maxHp);
  assert.equal(rt.player.dead, false);
});

test("checkpoint: menyimpan id checkpoint dan menyembuhkan pemain", () => {
  const rt = createRuntime(freshState());
  rt.state.world.checkpoint = "lain";
  rt.player.hp = 40;
  const events = sim(rt, 0.5);
  assert.ok(events.some((e) => e.type === "checkpoint" && e.id === "camp_start"));
  assert.equal(rt.state.world.checkpoint, "camp_start");
  assert.ok(rt.player.hp > 40, "regenerasi di dekat api unggun");
});

test("gerbang: memicu event pindah zona dan travelTo membangun ulang zona", () => {
  const rt = createRuntime(freshState());
  rt.player.x = rt.zone.width - 40;
  const events = sim(rt, 0.1, { right: true });
  const gate = events.find((e) => e.type === "gate");
  assert.ok(gate, "event gate muncul");
  assert.equal(gate.to, "old_road");

  travelTo(rt, gate.to);
  assert.equal(rt.zone.id, "old_road");
  assert.equal(rt.state.world.zone, "old_road");
  assert.ok(rt.enemies.length >= 5, "musuh zona baru di-spawn");
  assert.equal(rt.player.y, rt.zone.groundY);
});

test("boss: musuh boss tidak respawn setelah dikalahkan", () => {
  const state = freshState({ world: { zone: "ruins" } });
  const zone = zoneById("ruins");
  const before = spawnEnemies(state, zone, false);
  assert.ok(before.some((e) => e.boss), "boss ada di reruntuhan");

  state.world.flags.boss_warden_defeated = true;
  const after = spawnEnemies(state, zone, false);
  assert.ok(!after.some((e) => e.boss), "boss tidak muncul lagi");

  const rt = createRuntime(freshState({ world: { zone: "ruins" } }));
  const boss = rt.enemies.find((e) => e.boss);
  assert.equal(boss.bossFlag, "boss_warden_defeated");
  boss.hp = 1;
  rt.player.x = boss.x - 30;
  const events = sim(rt, 1, { attack: true });
  assert.ok(events.some((e) => e.type === "boss_defeated"), "flag boss terpasang saat tumbang");
  assert.equal(rt.state.world.flags.boss_warden_defeated, true);
});

test("snapshot: payload save sesuai kontrak backend dan tidak NaN", () => {
  const rt = createRuntime(freshState());
  sim(rt, 2.5, { right: true, run: true });
  const payload = snapshot(rt);
  assert.equal(payload.version, "1");
  for (const key of ["x", "y", "hp", "maxHp", "level", "xp", "skillLevel", "facing"]) {
    assert.equal(Number.isFinite(payload.player[key]), true, `player.${key} angka valid`);
  }
  assert.equal(typeof payload.world.zone, "string");
  assert.equal(typeof payload.world.checkpoint, "string");
  assert.equal(Array.isArray(payload.player.skills), true);
  assert.ok(payload.stats.playtime >= 2, "playtime tercatat");
  assert.equal(payload.x !== undefined, false, "tidak ada field liar");
  // bentuk yang dikirim ke server harus lewat validator server (diuji di api.test.mjs)
  assert.ok(payload.player.hp <= payload.player.maxHp);
});

test("normalizeState: data rusak / kosong tetap aman", () => {
  const fallback = normalizeState(undefined);
  assert.equal(fallback.player.level, 1);
  assert.equal(fallback.player.hp, 100);
  assert.equal(fallback.player.skills.length, 0);

  const messy = normalizeState({
    player: { level: 999, hp: 99999, maxHp: 10, xp: -50, skills: ["aura_slash", "cheat_skill"], facing: -1 },
    world: { zone: "tidak_ada", checkpoint: 123, flags: { ok: true, "bad key!": true }, defeated: { slime: -3 } },
    stats: { playtime: "abc", deaths: -1 },
  });
  assert.equal(messy.player.level, 99);
  assert.equal(messy.player.hp, 10, "hp dibatasi maxHp");
  assert.equal(messy.player.xp, 0);
  assert.deepEqual(messy.player.skills, ["aura_slash"], "skill tak dikenal dibuang");
  assert.equal(messy.world.zone, "greenwood", "zona tak dikenal jatuh ke default");
  assert.equal(messy.world.checkpoint, "camp_start");
  assert.deepEqual(Object.keys(messy.world.flags), ["ok"]);
  assert.equal(messy.world.defeated.slime, 0);
  assert.equal(messy.stats.playtime, 0);
  assert.equal(messy.stats.deaths, 0);
});

test("progressInfo: rasio XP untuk HUD selalu 0..1", () => {
  const state = freshState();
  state.player.xp = xpNeeded(1) / 2;
  const info = progressInfo(state);
  assert.equal(info.ratio, 0.5);
  assert.equal(info.skillUnlocked, false);
  assert.equal(serializeState(state, 10).stats.playtime, 10);
});

test("simulasi panjang tetap stabil (tanpa lonjakan posisi/NaN)", () => {
  const rt = createRuntime(freshState());
  const input = inputWith({ right: true, run: true, jump: true, attack: true });
  for (let i = 0; i < 60 * 20; i += 1) {
    simulate(rt, input, DT);
    assert.equal(Number.isFinite(rt.player.x), true);
    assert.equal(Number.isFinite(rt.player.y), true);
    assert.ok(rt.player.x >= 0 && rt.player.x <= rt.zone.width);
    input.jump = i % 40 < 8;
    input.attack = i % 30 < 6;
  }
  assert.ok(rt.playtime >= 19, `playtime berjalan (${rt.playtime.toFixed(1)}s)`);
  assert.equal(snapshot(rt).stats.playtime, Math.round(rt.playtime), "snapshot menyinkronkan playtime ke state");
});
