/**
 * game.js — runtime game: input, render Canvas 2D, HUD, menu, dan autosave ke backend C#.
 * Aturan gameplay ada di core.js (murni), grafis karakter dari atlas.js.
 */

import {
  COMBAT,
  ENEMY_TYPES,
  ZONES,
  createRuntime,
  createState,
  normalizeState,
  progressInfo,
  simulate,
  snapshot,
  travelTo,
  zoneById,
} from "./core.js";
import { Animator, SpriteSheet } from "./atlas.js";
import { api } from "./api.js";

const VIEW = { w: 480, h: 270 };
const AUTOSAVE_INTERVAL = 30;
const ZONE_ORDER = ZONES.map((z) => z.id);

const canvas = document.getElementById("game");
const ctx = canvas.getContext("2d");
ctx.imageSmoothingEnabled = false;

const ui = {
  overlay: document.getElementById("overlay"),
  menuPanel: document.getElementById("menu-panel"),
  profileList: document.getElementById("profile-list"),
  newProfileForm: document.getElementById("new-profile-form"),
  newProfileName: document.getElementById("new-profile-name"),
  menuStatus: document.getElementById("menu-status"),
  pausePanel: document.getElementById("pause-panel"),
  saveSlots: document.getElementById("save-slots"),
  loadSlots: document.getElementById("load-slots"),
  pauseStatus: document.getElementById("pause-status"),
  journeyPanel: document.getElementById("journey-panel"),
  journeyBody: document.getElementById("journey-body"),
  victoryPanel: document.getElementById("victory-panel"),
  victoryBody: document.getElementById("victory-body"),
  toast: document.getElementById("toast"),
  hud: document.getElementById("hud"),
  loading: document.getElementById("loading"),
};

const game = {
  mode: "boot", // boot | menu | playing | paused | journey | victory
  sheet: null,
  animator: null,
  rt: null,
  profile: null,
  profileName: "",
  slot: 1,
  paused: false,
  saveState: { busy: false, lastSavedAt: null, error: null, status: "" },
  notice: null,
  hintTime: 10,
  frame: 0,
};

// --------------------------------------------------------------------------- //
// input
// --------------------------------------------------------------------------- //
const KEY_MAP = {
  ArrowLeft: "left",
  KeyA: "left",
  ArrowRight: "right",
  KeyD: "right",
  ShiftLeft: "run",
  ShiftRight: "run",
  Space: "jump",
  ArrowUp: "jump",
  KeyW: "jump",
  KeyJ: "attack",
  KeyZ: "attack",
  KeyK: "skill",
  KeyX: "skill",
};

const input = { left: false, right: false, run: false, jump: false, attack: false, skill: false };

function setKey(code, pressed) {
  const action = KEY_MAP[code];
  if (!action) return false;
  input[action] = pressed;
  return true;
}

window.addEventListener("keydown", (event) => {
  if (setKey(event.code, true)) event.preventDefault();
  if (event.code === "Escape") togglePause();
  if (event.code === "KeyF" && game.mode === "playing") quickSave();
});

window.addEventListener("keyup", (event) => {
  if (setKey(event.code, false)) event.preventDefault();
});

// --------------------------------------------------------------------------- //
// startup
// --------------------------------------------------------------------------- //
async function boot() {
  try {
    game.sheet = await SpriteSheet.load();
    game.animator = new Animator(game.sheet);
  } catch (error) {
    ui.loading.textContent = `Gagal memuat sprite: ${error.message}. Jalankan: python3 tools/make_spritesheet.py`;
    return;
  }
  ui.loading.classList.add("hidden");
  canvas.focus();
  await showMenu();
  requestAnimationFrame(loop);
}

async function showMenu() {
  game.mode = "menu";
  game.paused = false;
  setPanel("menu");
  ui.profileList.innerHTML = "<p class=\"muted\">Memuat profil…</p>";
  try {
    const profiles = await api.listProfiles();
    renderProfiles(profiles);
    const health = await api.health();
    ui.menuStatus.textContent = `Terhubung ke backend C# • ${health.slots} slot save • db: ${shortPath(health.database)}`;
  } catch (error) {
    ui.profileList.innerHTML = "";
    ui.menuStatus.textContent = `Backend tidak terjangkau: ${error.message}. Jalankan: dotnet run --project server/RpgServer`;
  }
}

function shortPath(path) {
  if (!path) return "-";
  const parts = String(path).split("/");
  return parts.slice(-2).join("/");
}

function renderProfiles(profiles) {
  if (!profiles.length) {
    ui.profileList.innerHTML = "<p class=\"muted\">Belum ada profil. Buat profil baru untuk mulai perjalanan.</p>";
    return;
  }
  ui.profileList.innerHTML = "";
  for (const profile of profiles) {
    const card = document.createElement("div");
    card.className = "profile";
    card.dataset.profileId = profile.id;
    card.innerHTML = `
      <div class="profile-main">
        <strong>${escapeHtml(profile.name)}</strong>
        <span class="muted">Lv ${profile.level} • ${escapeHtml(profile.zone)} • ${escapeHtml(profile.checkpoint)}</span>
        <span class="muted small">${formatTime(profile.playtime)} bermain • ${profile.kills ?? 0} kill • ${profile.deaths ?? 0} mati • ${profile.slotCount} slot</span>
      </div>
      <div class="profile-actions">
        <button data-action="resume">Lanjutkan</button>
        <button data-action="slots" class="secondary">Slot</button>
        <button data-action="delete" class="danger">Hapus</button>
      </div>`;
    card.querySelector('[data-action="resume"]').addEventListener("click", () => resumeProfile(profile.id));
    card.querySelector('[data-action="slots"]').addEventListener("click", () => openSlotPicker(profile.id));
    card.querySelector('[data-action="delete"]').addEventListener("click", async () => {
      if (!confirm(`Hapus profil "${profile.name}" beserta semua save-nya?`)) return;
      await api.deleteProfile(profile.id);
      await showMenu();
    });
    ui.profileList.appendChild(card);
  }
}

ui.newProfileForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const name = ui.newProfileName.value.trim();
  if (!name) return;
  try {
    const profile = await api.createProfile(name);
    ui.newProfileName.value = "";
    game.profile = profile;
    game.profileName = profile.name;
    startNewRun();
    await autoSave("mulai perjalanan");
  } catch (error) {
    ui.menuStatus.textContent = `Gagal membuat profil: ${error.message}`;
  }
});

async function resumeProfile(profileId) {
  try {
    const profile = await api.getProfile(profileId);
    game.profile = profile.profile;
    game.profileName = profile.profile.name;
    const resume = await api.resume(profileId);
    if (!resume.slot) {
      startNewRun();
      await autoSave("perjalanan baru");
      return;
    }
    game.slot = resume.slot;
    enterGame(normalizeState(resume.state), `lanjut dari slot ${resume.slot}`);
  } catch (error) {
    ui.menuStatus.textContent = `Gagal melanjutkan: ${error.message}`;
  }
}

async function openSlotPicker(profileId) {
  const profile = await api.getProfile(profileId);
  game.profile = profile.profile;
  game.profileName = profile.profile.name;
  game.mode = "menu";
  setPanel("pause");
  ui.pauseStatus.textContent = "Pilih slot untuk dilanjutkan";
  renderSlots(profile.slots, "load", profileId);
  ui.saveSlots.innerHTML = "<p class=\"muted\">Pilih slot di atas untuk melanjutkan perjalanan.</p>";
}

function startNewRun() {
  const state = createState(game.profile.id, game.profileName, "greenwood");
  enterGame(state, "perjalanan baru dimulai");
}

function enterGame(state, notice) {
  game.rt = createRuntime(state);
  game.animator.reset("idle");
  game.mode = "playing";
  game.paused = false;
  game.hintTime = 10;
  game.notice = notice ? { text: notice, time: 3 } : null;
  setPanel(null);
}

// --------------------------------------------------------------------------- //
// loop
// --------------------------------------------------------------------------- //
let lastTime = 0;
function loop(timestamp) {
  const dt = Math.min(0.05, lastTime ? (timestamp - lastTime) / 1000 : 0.016);
  lastTime = timestamp;
  game.frame += 1;

  if (game.mode === "playing") {
    const events = simulate(game.rt, input, dt);
    handleEvents(events);
    updateCamera(dt);
    autosaveTick(dt);
  } else if (game.rt) {
    updateCamera(dt);
  }

  render(dt);
  requestAnimationFrame(loop);
}

function handleEvents(events) {
  for (const event of events) {
    switch (event.type) {
      case "checkpoint":
        notify(`Checkpoint: ${event.name} — progres disimpan`);
        autoSave("checkpoint");
        break;
      case "gate": {
        const target = ZONE_ORDER[ZONE_ORDER.indexOf(game.rt.zone.id) + 1];
        if (target) {
          travelTo(game.rt, target);
          notify(`Memasuki ${game.rt.zone.name}`);
          autoSave("pindah zona");
        }
        break;
      }
      case "level_up":
        notify(`Naik ke level ${event.level}! HP maksimum bertambah`);
        break;
      case "boss_defeated":
        notify(`${event.name} tumbang! Perjalanan selesai.`);
        autoSave("boss kalah");
        showVictory();
        break;
      case "kill":
        break;
      case "player_died":
        notify("Arga tumbang… kembali ke checkpoint");
        break;
      case "respawn":
        autoSave("setelah tumbang");
        break;
      case "skill_no_energy":
        notify("Energi tidak cukup untuk skill");
        break;
      default:
        break;
    }
  }
}

function updateCamera(dt) {
  if (!game.rt) return;
  const zone = game.rt.zone;
  const p = game.rt.player;
  const targetX = clamp(p.x - VIEW.w * 0.42, 0, Math.max(0, zone.width - VIEW.w));
  const targetY = clamp(p.y - VIEW.h * 0.72, zone.groundY - 230, zone.groundY - 110);
  const lerp = Math.min(1, dt * 6);
  game.rt.camera.x += (targetX - game.rt.camera.x) * lerp;
  game.rt.camera.y += (targetY - game.rt.camera.y) * lerp;
}

function autosaveTick(dt) {
  game.rt.autosaveTimer = (game.rt.autosaveTimer ?? AUTOSAVE_INTERVAL) - dt;
  if (game.rt.autosaveTimer <= 0) {
    game.rt.autosaveTimer = AUTOSAVE_INTERVAL;
    autoSave("autosave");
  }
}

// --------------------------------------------------------------------------- //
// save / load
// --------------------------------------------------------------------------- //
async function saveToSlot(slot, reason = "manual") {
  if (!game.rt || !game.profile) return;
  game.slot = slot;
  await autoSave(reason);
  if (game.mode === "paused") {
    const slots = await api.listSlots(game.profile.id).catch(() => []);
    renderSlots(slots, "save", game.profile.id);
  }
}

async function autoSave(reason) {
  if (!game.rt || !game.profile || game.saveState.busy) return;
  game.saveState.busy = true;
  try {
    const payload = snapshot(game.rt);
    const result = await api.saveSlot(game.profile.id, game.slot, payload);
    game.saveState.lastSavedAt = new Date();
    game.saveState.error = null;
    game.saveState.status = `tersimpan di slot ${result.slot}`;
    if (game.mode === "paused") {
      ui.pauseStatus.textContent = `Tersimpan (${reason}) di slot ${result.slot} — ${timestamp()}`;
      const slots = await api.listSlots(game.profile.id).catch(() => []);
      renderSlots(slots, "save", game.profile.id);
    }
  } catch (error) {
    game.saveState.error = error.message;
    game.saveState.status = "gagal menyimpan";
    if (game.mode === "paused") ui.pauseStatus.textContent = `Gagal menyimpan: ${error.message}`;
    else notify(`Gagal menyimpan: ${error.message}`);
  } finally {
    game.saveState.busy = false;
  }
}

function quickSave() {
  autoSave("manual (F)");
  notify(`Menyimpan ke slot ${game.slot}…`);
}

async function loadSlot(slot) {
  try {
    const data = await api.loadSlot(game.profile.id, slot);
    game.slot = slot;
    enterGame(normalizeState(data.state), `memuat slot ${slot}`);
    notify(`Slot ${slot} dimuat — ${data.info.zone} (Lv ${data.info.level})`);
  } catch (error) {
    ui.pauseStatus.textContent = `Gagal memuat: ${error.message}`;
  }
}

function renderSlots(slots, mode, profileId) {
  const container = mode === "save" ? ui.saveSlots : ui.loadSlots;
  container.innerHTML = "";
  for (let slot = 1; slot <= 3; slot += 1) {
    const info = slots.find((s) => s.slot === slot);
    const row = document.createElement("div");
    row.className = "slot";
    row.innerHTML = `
      <span class="slot-name">Slot ${slot}</span>
      <span class="muted small">${
        info
          ? `Lv ${info.level} • ${escapeHtml(info.zone)} • ${escapeHtml(info.checkpoint)} • ${formatTime(info.playtime)} • ${info.kills} kill`
          : "kosong"
      }</span>`;
    const saveButton = document.createElement("button");
    saveButton.textContent = info ? "Timpa" : "Simpan";
    saveButton.addEventListener("click", () => saveToSlot(slot, "manual"));
    const loadButton = document.createElement("button");
    loadButton.textContent = "Muat";
    loadButton.className = "secondary";
    loadButton.disabled = !info;
    loadButton.addEventListener("click", () => loadSlot(slot));
    const deleteButton = document.createElement("button");
    deleteButton.textContent = "Hapus";
    deleteButton.className = "danger";
    deleteButton.disabled = !info;
    deleteButton.addEventListener("click", async () => {
      await api.deleteSlot(profileId, slot);
      const updated = await api.listSlots(profileId);
      renderSlots(updated, mode, profileId);
    });
    row.append(saveButton, loadButton, deleteButton);
    container.appendChild(row);
  }
}

// --------------------------------------------------------------------------- //
// panels
// --------------------------------------------------------------------------- //
function setPanel(name) {
  ui.overlay.classList.toggle("hidden", !name);
  for (const [key, element] of Object.entries({
    menu: ui.menuPanel,
    pause: ui.pausePanel,
    journey: ui.journeyPanel,
    victory: ui.victoryPanel,
  })) {
    element.classList.toggle("hidden", key !== name);
  }
}

function togglePause() {
  if (game.mode === "playing") {
    game.mode = "paused";
    setPanel("pause");
    ui.pauseStatus.textContent = `Slot aktif: ${game.slot} • ${game.saveState.status || "belum menyimpan sesi ini"}`;
    refreshSlots();
  } else if (game.mode === "paused") {
    game.mode = "playing";
    setPanel(null);
  } else if (game.mode === "journey") {
    game.mode = "paused";
    setPanel("pause");
  } else if (game.mode === "victory") {
    game.mode = "paused";
    setPanel("pause");
  }
}

async function refreshSlots() {
  try {
    const slots = await api.listSlots(game.profile.id);
    renderSlots(slots, "save", game.profile.id);
    renderSlots(slots, "load", game.profile.id);
  } catch (error) {
    ui.pauseStatus.textContent = `Gagal memuat daftar slot: ${error.message}`;
  }
}

async function showJourney() {
  game.mode = "journey";
  setPanel("journey");
  ui.journeyBody.innerHTML = "<p class=\"muted\">Memuat catatan perjalanan…</p>";
  try {
    const data = await api.journey(game.profile.id);
    const info = progressInfo(game.rt.state);
    const events = data.events
      .map((event) => `<li><span class="kind">${escapeHtml(event.kind)}</span> ${escapeHtml(event.detail)} <span class="muted small">${new Date(event.createdAt).toLocaleString()}</span></li>`)
      .join("");
    ui.journeyBody.innerHTML = `
      <div class="journey-grid">
        <div><span class="muted small">Profil</span><strong>${escapeHtml(data.profile.name)}</strong></div>
        <div><span class="muted small">Level</span><strong>${info.level}</strong></div>
        <div><span class="muted small">XP</span><strong>${info.xp} / ${info.need}</strong></div>
        <div><span class="muted small">Zona sekarang</span><strong>${escapeHtml(data.profile.zone)}</strong></div>
        <div><span class="muted small">Checkpoint</span><strong>${escapeHtml(data.profile.checkpoint)}</strong></div>
        <div><span class="muted small">Boss kalah</span><strong>${data.profile.bosses}</strong></div>
        <div><span class="muted small">Waktu bermain</span><strong>${formatTime(data.profile.playtime)}</strong></div>
        <div><span class="muted small">Kill / mati</span><strong>${info.kills} / ${data.profile.deaths}</strong></div>
      </div>
      <h4>Jejak perjalanan</h4>
      <ul class="events">${events || "<li class=\"muted\">Belum ada catatan</li>"}</ul>`;
  } catch (error) {
    ui.journeyBody.innerHTML = `<p class="muted">Gagal memuat: ${escapeHtml(error.message)}</p>`;
  }
}

function showVictory() {
  game.mode = "victory";
  const info = progressInfo(game.rt.state);
  ui.victoryBody.innerHTML = `
    <p>Ruin Warden tumbang. Arga menuntaskan perjalanan di <strong>${escapeHtml(game.rt.zone.name)}</strong>.</p>
    <p class="muted">Level ${info.level} • ${info.xp}/${info.need} XP • ${info.kills} kill • ${formatTime(info.playtime)} bermain</p>
    <p class="muted small">Progres sudah tersimpan otomatis di slot ${game.slot}. Kamu bisa lanjut menjelajah bebas.</p>`;
  setPanel("victory");
}

document.getElementById("pause-resume").addEventListener("click", togglePause);
document.getElementById("pause-journey").addEventListener("click", showJourney);
document.getElementById("journey-back").addEventListener("click", () => {
  game.mode = "paused";
  setPanel("pause");
  refreshSlots();
});
document.getElementById("victory-back").addEventListener("click", () => {
  game.mode = "paused";
  setPanel("pause");
  refreshSlots();
});
document.getElementById("pause-menu").addEventListener("click", async () => {
  await autoSave("kembali ke menu");
  await showMenu();
});

function notify(text) {
  game.notice = { text, time: 3 };
}

// --------------------------------------------------------------------------- //
// render
// --------------------------------------------------------------------------- //
function render(dt) {
  ctx.clearRect(0, 0, VIEW.w, VIEW.h);
  const rt = game.rt;
  if (!rt) {
    drawMenuBackdrop(dt);
    return;
  }
  const camX = Math.round(rt.camera.x);
  const camY = Math.round(rt.camera.y);
  drawSky(rt.zone, camY);
  drawParallax(rt.zone, camX, camY);
  drawGround(rt.zone, camX, camY);
  drawCheckpoint(rt.zone, camX, camY, rt);
  drawGates(rt.zone, camX, camY);
  drawEffects(rt, camX, camY, dt);
  for (const enemy of rt.enemies) drawEnemy(enemy, camX, camY, rt);
  drawPlayer(rt, camX, camY, dt);
  drawHud(rt);
  if (game.hintTime > 0) game.hintTime -= dt;
}

function drawMenuBackdrop(dt) {
  ctx.fillStyle = "#101725";
  ctx.fillRect(0, 0, VIEW.w, VIEW.h);
  if (game.sheet) {
    game.animator.setState("idle");
    const frame = game.animator.update(dt);
    game.sheet.drawScaled(ctx, frame, VIEW.w / 2, VIEW.h / 2 + 40, 1.4, 1);
  }
}

function drawSky(zone, camY) {
  const gradient = ctx.createLinearGradient(0, 0, 0, VIEW.h);
  gradient.addColorStop(0, zone.sky[0]);
  gradient.addColorStop(1, zone.sky[1]);
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, VIEW.w, VIEW.h);
  ctx.fillStyle = "rgba(255,255,255,0.06)";
  for (let i = 0; i < 26; i += 1) {
    const x = (i * 71) % VIEW.w;
    const y = (i * 37) % 120;
    ctx.fillRect(x, y, 2, 2);
  }
  void camY;
}

function drawParallax(zone, camX, camY) {
  const baseY = zone.groundY - camY;
  ctx.fillStyle = zone.hill;
  for (let i = -1; i < 8; i += 1) {
    const x = i * 160 - ((camX * 0.35) % 160);
    ctx.beginPath();
    ctx.moveTo(x, baseY);
    ctx.lineTo(x + 80, baseY - 90);
    ctx.lineTo(x + 160, baseY);
    ctx.closePath();
    ctx.fill();
  }
  ctx.fillStyle = "rgba(0,0,0,0.18)";
  ctx.fillRect(0, baseY - 4, VIEW.w, 4);
}

function drawGround(zone, camX, camY) {
  const y = zone.groundY - camY;
  ctx.fillStyle = zone.ground[0];
  ctx.fillRect(0, y, VIEW.w, VIEW.h - y);
  ctx.fillStyle = zone.ground[1];
  ctx.fillRect(0, y + 10, VIEW.w, VIEW.h - y);
  for (const platform of zone.platforms) {
    const px = platform.x - camX;
    if (px > VIEW.w + 40 || px + platform.w < -40) continue;
    const py = platform.y - camY;
    ctx.fillStyle = zone.ground[0];
    ctx.fillRect(px, py, platform.w, 8);
    ctx.fillStyle = zone.ground[1];
    ctx.fillRect(px, py + 8, platform.w, 6);
    ctx.fillStyle = "rgba(0,0,0,0.25)";
    ctx.fillRect(px, py + 14, platform.w, 3);
  }
}

function drawCheckpoint(zone, camX, camY, rt) {
  const x = zone.checkpoint.x - camX;
  const y = zone.groundY - camY;
  ctx.fillStyle = "#6b4a2b";
  ctx.fillRect(x - 2, y - 12, 4, 12);
  const flicker = Math.sin(rt.time * 9) * 2;
  ctx.fillStyle = "#ffb347";
  ctx.fillRect(x - 5, y - 22 + flicker * 0.2, 10, 10);
  ctx.fillStyle = "#ffe08a";
  ctx.fillRect(x - 2, y - 19 + flicker * 0.2, 4, 5);
  if (rt.state.world.checkpoint === zone.checkpoint.id) {
    ctx.fillStyle = "rgba(255,224,138,0.75)";
    ctx.fillRect(x - 26, y - 40, 52, 2);
  }
}

function drawGates(zone, camX, camY) {
  for (const gate of zone.gates) {
    const x = gate.x - camX;
    if (x < -60 || x > VIEW.w + 60) continue;
    const y = zone.groundY - camY;
    ctx.fillStyle = "#2b2233";
    ctx.fillRect(x, y - 70, 14, 70);
    ctx.fillStyle = "#89e3ff";
    ctx.fillRect(x + 5, y - 62, 4, 54);
    ctx.fillStyle = "#ffffff";
    ctx.font = "8px monospace";
    ctx.fillText(`→ ${gate.label}`, x - 26, y - 78);
  }
}

function drawEffects(rt, camX, camY, dt) {
  void dt;
  for (const effect of rt.effects) {
    const ratio = effect.life / effect.maxLife;
    if (effect.type === "aura") {
      ctx.strokeStyle = `rgba(143,227,255,${0.75 * ratio})`;
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.arc(effect.x - camX, effect.y - camY + 20, effect.radius * (1.15 - ratio * 0.35), 0, Math.PI * 2);
      ctx.stroke();
      ctx.strokeStyle = `rgba(255,215,106,${0.6 * ratio})`;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.arc(effect.x - camX, effect.y - camY + 20, effect.radius * (1.4 - ratio * 0.5), 0, Math.PI * 2);
      ctx.stroke();
    } else if (effect.type === "levelup") {
      ctx.fillStyle = `rgba(255,224,138,${ratio})`;
      ctx.font = "10px monospace";
      ctx.fillText("LEVEL UP!", effect.x - camX - 22, effect.y - camY - (1 - ratio) * 24);
    }
  }
}

function drawPlayer(rt, camX, camY, dt) {
  const p = rt.player;
  const animator = game.animator;
  animator.setState(p.anim);
  const frame = animator.update(dt);
  const x = p.x - camX;
  const y = p.y - camY;
  if (p.invuln > 0 && Math.floor(rt.time * 18) % 2 === 0 && !p.dead) {
    ctx.globalAlpha = 0.45;
  }
  game.sheet.draw(ctx, frame, x, y, p.facing);
  ctx.globalAlpha = 1;
  if (p.flash > 0) {
    ctx.fillStyle = `rgba(255,80,80,${p.flash * 1.6})`;
    ctx.fillRect(x - 12, y - 46, 24, 46);
  }
  if (p.dead) {
    ctx.fillStyle = "rgba(0,0,0,0.5)";
    ctx.fillRect(0, 0, VIEW.w, VIEW.h);
    ctx.fillStyle = "#ffe08a";
    ctx.font = "12px monospace";
    ctx.fillText("Arga tumbang — kembali ke checkpoint…", 96, 130);
  }
}

function drawEnemy(enemy, camX, camY, rt) {
  const x = enemy.x - camX;
  const y = enemy.y - camY;
  if (x < -80 || x > VIEW.w + 80) return;
  if (enemy.dead) {
    if (enemy.deadTimer > 1.2) return;
    ctx.globalAlpha = Math.max(0, 1 - enemy.deadTimer / 1.2);
    ctx.fillStyle = "#555";
    ctx.fillRect(x - enemy.width / 2, y - 6, enemy.width, 6);
    ctx.globalAlpha = 1;
    return;
  }
  const flash = enemy.hurtTimer > 0;
  if (enemy.type === "slime") {
    const squash = 1 + Math.sin(enemy.anim * 3) * 0.12;
    ctx.fillStyle = flash ? "#ffd0d0" : "#5ac18e";
    ctx.fillRect(x - 13, y - 22 * squash, 26, 22 * squash);
    ctx.fillStyle = flash ? "#ffffff" : "#3d8f68";
    ctx.fillRect(x - 13, y - 6, 26, 6);
    ctx.fillStyle = "#12261d";
    ctx.fillRect(x - 8, y - 16 * squash, 4, 4);
    ctx.fillRect(x + 4, y - 16 * squash, 4, 4);
  } else if (enemy.type === "bat") {
    const flap = Math.sin(enemy.anim * 12) * 6;
    ctx.fillStyle = flash ? "#ffd0d0" : "#7b5fa8";
    ctx.fillRect(x - 9, y - 16, 18, 14);
    ctx.fillRect(x - 18, y - 14 + flap, 9, 6);
    ctx.fillRect(x + 9, y - 14 - flap, 9, 6);
    ctx.fillStyle = "#ffe08a";
    ctx.fillRect(x - 6, y - 13, 4, 3);
    ctx.fillRect(x + 2, y - 13, 4, 3);
  } else {
    ctx.fillStyle = flash ? "#ffd0d0" : "#4a4a63";
    ctx.fillRect(x - 23, y - 56, 46, 56);
    ctx.fillStyle = "#2c2c40";
    ctx.fillRect(x - 23, y - 56, 46, 8);
    ctx.fillStyle = "#c94b4b";
    ctx.fillRect(x - 14, y - 44, 8, 5);
    ctx.fillRect(x + 6, y - 44, 8, 5);
    ctx.fillStyle = "#6d6d8f";
    ctx.fillRect(x - 30, y - 40, 7, 40);
    ctx.fillRect(x + 23, y - 40, 7, 40);
  }
  if (enemy.hp < enemy.maxHp || enemy.boss) {
    const width = enemy.boss ? 120 : 30;
    const barX = x - width / 2;
    const barY = y - (enemy.boss ? 68 : 34);
    ctx.fillStyle = "rgba(0,0,0,0.6)";
    ctx.fillRect(barX, barY, width, 5);
    ctx.fillStyle = enemy.boss ? "#c94b4b" : "#8fe3ff";
    ctx.fillRect(barX, barY, width * Math.max(0, enemy.hp / enemy.maxHp), 5);
    if (enemy.boss) {
      ctx.fillStyle = "#ffffff";
      ctx.font = "8px monospace";
      ctx.fillText(enemy.name, barX, barY - 3);
    }
  }
  void rt;
}

// --------------------------------------------------------------------------- //
// HUD
// --------------------------------------------------------------------------- //
function drawHud(rt) {
  const p = rt.player;
  ctx.font = "8px monospace";
  const info = progressInfo(rt.state);
  // HP
  ctx.fillStyle = "rgba(0,0,0,0.55)";
  ctx.fillRect(8, 8, 150, 26);
  ctx.fillStyle = "#3a1f28";
  ctx.fillRect(12, 12, 140, 8);
  ctx.fillStyle = "#e05a5a";
  ctx.fillRect(12, 12, Math.max(0, 140 * (p.hp / p.maxHp)), 8);
  ctx.fillStyle = "#dfe7f5";
  ctx.fillText(`${Math.max(0, Math.round(p.hp))}/${p.maxHp}`, 12, 30);
  // energi skill
  ctx.fillStyle = "#1d2b3a";
  ctx.fillRect(60, 12, 92, 8);
  ctx.fillStyle = "#5ab0e0";
  ctx.fillRect(60, 12, 92 * (p.energy / 100), 8);
  ctx.fillStyle = "#dfe7f5";
  ctx.fillText(COMBAT.skillUnlockLevel <= info.level ? "skill: siap (K)" : `skill: lv ${COMBAT.skillUnlockLevel}`, 60, 30);
  // level + xp
  ctx.fillStyle = "rgba(0,0,0,0.55)";
  ctx.fillRect(8, 38, 150, 20);
  ctx.fillStyle = "#2f2a44";
  ctx.fillRect(12, 48, 140, 6);
  ctx.fillStyle = "#ffd76a";
  ctx.fillRect(12, 48, Math.max(0, 140 * info.ratio), 6);
  ctx.fillStyle = "#dfe7f5";
  ctx.fillText(`Lv ${info.level}`, 12, 46);
  ctx.fillText(`${info.xp}/${info.need} xp`, 78, 46);
  // kanan atas: zona, slot, save
  const zoneText = `${rt.zone.name}`;
  ctx.fillStyle = "rgba(0,0,0,0.55)";
  ctx.fillRect(VIEW.w - 168, 8, 160, 34);
  ctx.fillStyle = "#dfe7f5";
  ctx.fillText(zoneText, VIEW.w - 164, 20);
  ctx.fillStyle = "#9ad6a0";
  ctx.fillText(`slot ${game.slot} • ${game.saveState.status || "belum disimpan"}`, VIEW.w - 164, 32);
  // hint kontrol
  if (game.hintTime > 0) {
    ctx.fillStyle = "rgba(0,0,0,0.55)";
    ctx.fillRect(8, VIEW.h - 34, 300, 26);
    ctx.fillStyle = "#dfe7f5";
    ctx.fillText("←/→ atau A/D jalan • Shift lari • Spasi lompat", 12, VIEW.h - 22);
    ctx.fillText("J serang • K skill • F simpan cepat • Esc menu", 12, VIEW.h - 12);
  }
  if (game.notice && game.notice.time > 0) {
    game.notice.time -= 1 / 60;
    ctx.fillStyle = "rgba(0,0,0,0.65)";
    const width = Math.min(VIEW.w - 40, game.notice.text.length * 6 + 16);
    ctx.fillRect(VIEW.w / 2 - width / 2, 74, width, 18);
    ctx.fillStyle = "#ffe08a";
    ctx.fillText(game.notice.text, VIEW.w / 2 - width / 2 + 8, 86);
  }
}

// --------------------------------------------------------------------------- //
// util
// --------------------------------------------------------------------------- //
function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function formatTime(seconds) {
  const total = Math.max(0, Math.round(seconds || 0));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h > 0 ? `${h}j ${m}m` : m > 0 ? `${m}m ${s}s` : `${s}s`;
}

function timestamp() {
  return new Date().toLocaleTimeString();
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (char) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;",
  })[char]);
}

// Simpan terakhir saat halaman ditutup/di-reload.
// sendBeacon hanya bisa POST (server ini memakai PUT), jadi pakai fetch keepalive.
window.addEventListener("beforeunload", () => {
  if (!game.rt || !game.profile) return;
  try {
    fetch(`/api/profiles/${game.profile.id}/slots/${game.slot}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(snapshot(game.rt)),
      keepalive: true,
    }).catch(() => {});
  } catch {
    /* halaman sudah ditutup, tidak ada yang bisa dilakukan */
  }
});

document.getElementById("pause-save").addEventListener("click", () => saveToSlot(game.slot, "manual"));
document.getElementById("pause-quicksave").addEventListener("click", () => {
  saveToSlot(game.slot, "manual").then(() => notify(`Tersimpan di slot ${game.slot}`));
});

// hook untuk test headless
window.ARGA = {
  input,
  api,
  zones: ZONES,
  enemyTypes: ENEMY_TYPES,
  get mode() {
    return game.mode;
  },
  get slot() {
    return game.slot;
  },
  get runtime() {
    return game.rt;
  },
  get profile() {
    return game.profile;
  },
  get animator() {
    return game.animator;
  },
  get saveState() {
    return game.saveState;
  },
  enterGame,
  startNewRun,
  showMenu,
  autoSave,
  saveToSlot,
  loadSlot,
  simulate: (seconds, held = {}) => {
    const steps = Math.max(1, Math.round(seconds * 60));
    for (const key of Object.keys(input)) input[key] = Boolean(held[key]);
    const events = [];
    for (let i = 0; i < steps; i += 1) events.push(...simulate(game.rt, input, 1 / 60));
    for (const key of Object.keys(input)) input[key] = false;
    return events;
  },
  setState: (state) => enterGame(normalizeState(state), "test"),
  currentFrame: () => game.animator.frameName,
  setSlot: (slot) => {
    game.slot = slot;
  },
};

boot();
