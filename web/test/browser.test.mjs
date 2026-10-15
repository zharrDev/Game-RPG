/**
 * browser.test.mjs — tes end-to-end lewat interface yang benar-benar dipakai pemain
 * (Chromium headless + canvas + keyboard + menu).
 *
 * Jalankan: RUN_BROWSER=1 RPG_BASE_URL=http://127.0.0.1:5099 node --test test/browser.test.mjs
 * Butuh server C# sudah jalan dan puppeteer terpasang (npm install).
 */

import { test } from "node:test";
import assert from "node:assert/strict";
import { writeFile } from "node:fs/promises";

const BASE = process.env.RPG_BASE_URL ?? "http://127.0.0.1:5099";
const RUN = process.env.RUN_BROWSER === "1";

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

test("browser: main game sungguhan — gerak, lari, lompat, serang, skill, simpan, lanjut setelah reload", { skip: !RUN }, async () => {
  const puppeteer = (await import("puppeteer")).default;
  const browser = await puppeteer.launch({
    headless: true,
    args: [
      "--no-sandbox",
      "--disable-setuid-sandbox",
      "--disable-background-timer-throttling",
      "--disable-renderer-backgrounding",
      "--disable-backgrounding-occluded-windows",
    ],
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1200, height: 720 });

  const errors = [];
  page.on("pageerror", (error) => errors.push(`pageerror: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(`console: ${message.text()}`);
  });

  try {
    // ---------- boot + load sprite ----------
    await page.goto(BASE, { waitUntil: "domcontentloaded" });
    await page.waitForFunction(() => window.ARGA && window.ARGA.mode === "menu", { timeout: 20000 });
    assert.match(
      await page.$eval("#menu-status", (el) => el.textContent),
      /Terhubung ke backend C#/,
      "klien tersambung ke backend C#",
    );
    await page.waitForFunction(() => document.getElementById("loading").classList.contains("hidden"), { timeout: 15000 });

    // ---------- buat profil lewat form ----------
    const name = `E2E-${Date.now().toString(36)}`;
    await page.type("#new-profile-name", name);
    await page.click("#new-profile-form button[type=submit]");
    await page.waitForFunction(() => window.ARGA.mode === "playing", { timeout: 15000 });
    assert.ok(await page.$eval("#overlay", (el) => el.classList.contains("hidden")), "overlay menu tertutup saat bermain");

    // perekam per-frame: memastikan frame animasi yang benar-benar tergambar, bukan cuma sampling timing
    await page.evaluate(() => {
      window.__samples = [];
      const tick = () => {
        const rt = window.ARGA.runtime;
        if (rt) {
          window.__samples.push({
            a: rt.player.anim,
            f: window.ARGA.animator.frameName,
            y: Math.round(rt.player.y),
            x: Math.round(rt.player.x),
            e: Math.round(rt.player.energy),
          });
        }
        if (window.__samples.length < 6000) requestAnimationFrame(tick);
      };
      requestAnimationFrame(tick);
    });
    const samplesSince = async (from) => page.evaluate((start) => window.__samples.slice(start), from);
    const sampleCount = () => page.evaluate(() => window.__samples.length);

    const start = await page.evaluate(() => ({
      x: window.ARGA.runtime.player.x,
      y: window.ARGA.runtime.player.y,
      ground: window.ARGA.runtime.zone.groundY,
      frame: window.ARGA.animator.frameName,
      zone: window.ARGA.runtime.zone.id,
    }));
    assert.equal(start.zone, "greenwood");
    assert.equal(start.y, start.ground);
    assert.match(start.frame ?? "", /idle_/, `frame idle tergambar (${start.frame})`);

    // ---------- jalan maju (kanan) ----------
    await page.keyboard.down("ArrowRight");
    await sleep(900);
    await page.keyboard.up("ArrowRight");
    const afterForward = await page.evaluate(() => ({ x: window.ARGA.runtime.player.x, anim: window.ARGA.runtime.player.anim }));
    assert.ok(afterForward.x > start.x + 60, `maju lewat keyboard (${start.x.toFixed(0)} -> ${afterForward.x.toFixed(0)})`);

    // ---------- mundur (kiri) + hadap kiri ----------
    await page.keyboard.down("ArrowLeft");
    await sleep(500);
    const facingLeft = await page.evaluate(() => window.ARGA.runtime.player.facing);
    await page.keyboard.up("ArrowLeft");
    assert.equal(facingLeft, -1, "sprites di-flip saat menghadap kiri");

    // ---------- lari lebih cepat ----------
    await page.keyboard.down("ArrowRight");
    await sleep(500);
    await page.keyboard.up("ArrowRight");
    const walkDistance = (await page.evaluate(() => window.ARGA.runtime.player.x)) - afterForward.x;
    await sleep(120);
    const beforeRun = await page.evaluate(() => window.ARGA.runtime.player.x);
    await page.keyboard.down("ShiftLeft");
    await page.keyboard.down("ArrowRight");
    await sleep(500);
    const runState = await page.evaluate(() => ({ x: window.ARGA.runtime.player.x, anim: window.ARGA.runtime.player.anim }));
    await page.keyboard.up("ArrowRight");
    await page.keyboard.up("ShiftLeft");
    const runDistance = runState.x - beforeRun;
    assert.ok(runDistance > walkDistance + 20, `lari lebih jauh dari jalan (${runDistance.toFixed(0)} vs ${walkDistance.toFixed(0)})`);
    assert.equal(runState.anim, "run");

    // ---------- lompat ----------
    const groundY = start.ground;
    const jumpFrom = await sampleCount();
    await page.keyboard.down("Space");
    await sleep(700);
    await page.keyboard.up("Space");
    await sleep(700);
    const jumpSamples = await samplesSince(jumpFrom);
    const jumpAnims = new Set(jumpSamples.map((s) => s.a));
    assert.ok(jumpAnims.has("jump_takeoff"), `frame crouch (jump_1) tampil — dapat: ${[...jumpAnims].join(",")}`);
    assert.ok(jumpAnims.has("jump_up"), "frame naik (jump_2) tampil di puncak");
    assert.ok(jumpAnims.has("jump_down"), "frame jatuh (jump_3) tampil");
    assert.ok(Math.min(...jumpSamples.map((s) => s.y)) < groundY - 60, "tubuh benar-benar terangkat");
    assert.equal(jumpSamples.at(-1).y, groundY, "mendarat kembali di tanah");

    // ---------- serang ----------
    const attackFrom = await sampleCount();
    await page.keyboard.down("KeyJ");
    await sleep(160);
    await page.keyboard.up("KeyJ");
    await sleep(350);
    const attackSamples = await samplesSince(attackFrom);
    assert.ok(attackSamples.some((s) => s.a === "attack"), "state serangan aktif");
    assert.ok(
      attackSamples.some((s) => /^attack_\d$/.test(s.f)),
      `frame pedang tergambar (${[...new Set(attackSamples.map((s) => s.f))].join(",")})`,
    );

    // ---------- skill terkunci di level 1 ----------
    const lockedFrom = await sampleCount();
    await page.keyboard.down("KeyK");
    await sleep(200);
    await page.keyboard.up("KeyK");
    const lockedSamples = await samplesSince(lockedFrom);
    const locked = await page.evaluate(() => ({ energy: window.ARGA.runtime.player.energy, level: window.ARGA.runtime.player.level }));
    assert.equal(locked.level, 1);
    assert.equal(Math.round(locked.energy), 100, "skill belum aktif di level 1");
    assert.ok(!lockedSamples.some((s) => s.a === "skill"), "animasi skill tidak jalan sebelum level 3");

    // ---------- naikkan level (seperti habis menang banyak) lalu pakai skill ----------
    await page.evaluate(() => {
      const state = window.ARGA.runtime.state;
      state.player.level = 3;
      state.player.maxHp = 120;
      state.player.hp = 120;
      state.player.skills = ["aura_slash"];
    });
    const skillFrom = await sampleCount();
    await page.keyboard.down("KeyK");
    await sleep(160);
    await page.keyboard.up("KeyK");
    const skillSamples = await samplesSince(skillFrom);
    assert.ok(skillSamples.some((s) => s.a === "skill"), "animasi skill jalan");
    assert.ok(skillSamples.some((s) => /^skill_\d$/.test(s.f)), "frame skill tergambar");
    assert.ok(Math.min(...skillSamples.map((s) => s.e)) <= 82, `energi terpakai untuk skill (${Math.min(...skillSamples.map((s) => s.e))})`);
    const effects = await page.evaluate(() => window.ARGA.runtime.effects.length);
    assert.ok(effects > 0, "efek aura muncul");
    await sleep(700);

    // ---------- simpan lewat menu pause (UI sungguhan) ----------
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => window.ARGA.mode === "paused" && !document.getElementById("pause-panel").classList.contains("hidden"));
    const beforeSave = await page.evaluate(() => ({ x: window.ARGA.runtime.player.x, level: window.ARGA.runtime.player.level }));
    await page.click("#save-slots .slot:nth-child(1) button"); // tombol "Simpan"/"Timpa" slot 1
    await page.waitForFunction(() => window.ARGA.saveState.status.includes("tersimpan"), { timeout: 10000 });
    const profileId = await page.evaluate(() => window.ARGA.profile.id);

    const apiState = await fetch(`${BASE}/api/profiles/${profileId}/slots/1`).then((r) => r.json());
    assert.equal(apiState.state.player.level, beforeSave.level, "level tersimpan di server");
    assert.ok(Math.abs(apiState.state.player.x - beforeSave.x) < 3, `posisi tersimpan (${apiState.state.player.x} vs ${beforeSave.x.toFixed(1)})`);
    assert.equal(apiState.state.world.zone, "greenwood");
    assert.ok(apiState.state.stats.playtime >= 1, "waktu bermain tercatat");

    // screenshot untuk dokumentasi
    await page.screenshot({ path: "/tmp/arga-pause.png" });
    await page.click("#pause-resume");
    await page.waitForFunction(() => window.ARGA.mode === "playing");
    await page.screenshot({ path: "/tmp/arga-gameplay.png" });

    // catatan perjalanan lewat UI
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => !document.getElementById("pause-panel").classList.contains("hidden"));
    await page.click("#pause-journey");
    await page.waitForFunction(() => window.ARGA.mode === "journey" && document.getElementById("journey-body").textContent.length > 40);
    const journeyText = await page.$eval("#journey-body", (el) => el.textContent);
    assert.match(journeyText, /Level/);
    assert.match(journeyText, /greenwood|Greenwood/);
    await page.screenshot({ path: "/tmp/arga-journey.png" });
    await page.click("#journey-back");

    // ---------- reload halaman: perjalanan harus lanjut dari titik tersimpan ----------
    await page.reload({ waitUntil: "domcontentloaded" });
    await page.waitForFunction(() => window.ARGA && window.ARGA.mode === "menu", { timeout: 20000 });
    const card = await page.$(`.profile[data-profile-id="${profileId}"]`);
    assert.ok(card, "profil tersimpan tampil lagi di menu");
    await card.$eval('[data-action="resume"]', (button) => button.click());
    await page.waitForFunction(() => window.ARGA.mode === "playing", { timeout: 15000 });
    const resumed = await page.evaluate(() => ({
      x: window.ARGA.runtime.player.x,
      level: window.ARGA.runtime.player.level,
      hp: window.ARGA.runtime.player.hp,
      zone: window.ARGA.runtime.zone.id,
    }));
    assert.ok(Math.abs(resumed.x - beforeSave.x) < 6, `posisi dilanjutkan dari save (${resumed.x.toFixed(1)} vs ${beforeSave.x.toFixed(1)})`);
    assert.equal(resumed.level, beforeSave.level, "level hasil main sebelumnya kembali");
    assert.equal(resumed.zone, "greenwood");
    assert.equal(resumed.hp, 120);

    // main lagi setelah dilanjutkan: memastikan runtime tetap sehat
    await page.keyboard.down("ArrowRight");
    await sleep(400);
    await page.keyboard.up("ArrowRight");
    const moved = await page.evaluate(() => window.ARGA.runtime.player.x);
    assert.ok(moved > resumed.x + 25, "pemain tetap bisa bergerak setelah memuat save");

    const criticalErrors = errors.filter((message) => !/favicon|404 \(Not Found\)/i.test(message));
    assert.deepEqual(criticalErrors, [], `tidak ada error di console browser (${criticalErrors.join(" | ")})`);

    await writeFile(
      "/tmp/arga-browser-result.json",
      JSON.stringify({ profileId, savedX: beforeSave.x, resumedX: resumed.x, screenshots: ["/tmp/arga-gameplay.png", "/tmp/arga-pause.png", "/tmp/arga-journey.png"] }, null, 2),
    );
  } finally {
    await browser.close();
  }
});
