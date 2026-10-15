/**
 * core.js — seluruh aturan game (murni, tanpa DOM) supaya bisa diuji headless.
 *
 * Isi: data dunia/zona, fisika pemain, combat, AI musuh, progresi level,
 * dan bentuk save-state yang dikirim ke backend C#.
 */

export const SAVE_VERSION = "1";

export const PHYSICS = {
  gravity: 1600,
  jumpVelocity: -500,
  walkSpeed: 96,
  runSpeed: 196,
  maxFall: 900,
  coyoteTime: 0.1,
  jumpBuffer: 0.12,
  bodyWidth: 22,
  bodyHeight: 46,
};

export const COMBAT = {
  attackDamage: 12,
  attackDuration: 0.34,
  attackActiveFrom: 0.07,
  attackActiveTo: 0.22,
  attackCooldown: 0.12,
  attackReach: 40,
  attackHeight: 40,
  skillUnlockLevel: 3,
  skillCost: 25,
  skillRegen: 7,
  skillDamage: 34,
  skillRadius: 74,
  skillDuration: 0.6,
  invulnTime: 0.8,
  knockback: 150,
  hurtStun: 0.35,
};

export const SKILLS = [
  {
    id: "aura_slash",
    name: "Aura Slash",
    unlockLevel: COMBAT.skillUnlockLevel,
    cost: COMBAT.skillCost,
    damage: COMBAT.skillDamage,
    description: "Tebasan aura melingkar, merusak semua musuh dalam radius 74px.",
  },
];

export const ENEMY_TYPES = {
  slime: { name: "Slime", hp: 30, xp: 18, contact: 8, speed: 34, width: 26, height: 22, patrolRange: 90 },
  bat: { name: "Cave Bat", hp: 24, xp: 24, contact: 10, speed: 62, width: 24, height: 18, chaseRange: 300 },
  warden: { name: "Ruin Warden", hp: 260, xp: 220, contact: 18, speed: 74, width: 46, height: 56, boss: true },
};

export const ZONES = [
  {
    id: "greenwood",
    name: "Greenwood Vale",
    width: 2600,
    groundY: 300,
    checkpoint: { id: "camp_start", x: 140, name: "Kemah Awal" },
    sky: ["#12303f", "#2a5d4a"],
    hill: "#1d4a3c",
    ground: ["#3c6b45", "#2a4c33"],
    platforms: [
      { x: 520, y: 240, w: 150 },
      { x: 760, y: 180, w: 120 },
      { x: 1180, y: 230, w: 180 },
      { x: 1620, y: 190, w: 140 },
      { x: 2050, y: 240, w: 170 },
    ],
    enemies: [
      { type: "slime", x: 640, patrol: [540, 780] },
      { type: "slime", x: 1500, patrol: [1380, 1650] },
      { type: "bat", x: 1000, y: 170 },
    ],
    gates: [{ x: 2520, to: "old_road", label: "Old Road" }],
  },
  {
    id: "old_road",
    name: "Old Road",
    width: 2900,
    groundY: 320,
    checkpoint: { id: "camp_road", x: 160, name: "Kemah Jalan Tua" },
    sky: ["#241a33", "#4a3357"],
    hill: "#2e2440",
    ground: ["#4d4359", "#343046"],
    platforms: [
      { x: 420, y: 250, w: 130 },
      { x: 700, y: 185, w: 110 },
      { x: 980, y: 250, w: 150 },
      { x: 1320, y: 200, w: 130 },
      { x: 1700, y: 250, w: 160 },
      { x: 2100, y: 190, w: 140 },
      { x: 2450, y: 240, w: 150 },
    ],
    enemies: [
      { type: "slime", x: 520, patrol: [420, 700] },
      { type: "slime", x: 1200, patrol: [1080, 1400] },
      { type: "bat", x: 900, y: 180 },
      { type: "bat", x: 1900, y: 170 },
      { type: "slime", x: 2300, patrol: [2200, 2500] },
    ],
    gates: [{ x: 2820, to: "ruins", label: "Reruntuhan Kuno" }],
  },
  {
    id: "ruins",
    name: "Reruntuhan Kuno",
    width: 2400,
    groundY: 300,
    checkpoint: { id: "camp_ruins", x: 150, name: "Kemah Reruntuhan" },
    sky: ["#1a1420", "#3c2233"],
    hill: "#2b1b29",
    ground: ["#4a3a44", "#332830"],
    platforms: [
      { x: 480, y: 240, w: 140 },
      { x: 820, y: 180, w: 120 },
      { x: 1250, y: 240, w: 150 },
      { x: 1650, y: 190, w: 130 },
    ],
    enemies: [
      { type: "bat", x: 700, y: 180 },
      { type: "slime", x: 1100, patrol: [980, 1300] },
      { type: "bat", x: 1500, y: 170 },
      { type: "warden", x: 2050, bossFlag: "boss_warden_defeated" },
    ],
    gates: [],
  },
];

// --------------------------------------------------------------------------- //
// progresi
// --------------------------------------------------------------------------- //
export function xpNeeded(level) {
  return 80 * level;
}

export function maxHpFor(level) {
  return 100 + 10 * (level - 1);
}

export function attackDamageOf(level) {
  return COMBAT.attackDamage + (level - 1) * 2;
}

export function skillDamageOf(level, skillLevel) {
  return COMBAT.skillDamage + (level - 1) * 4 + (skillLevel - 1) * 8;
}

export function unlockedSkills(level) {
  return SKILLS.filter((skill) => level >= skill.unlockLevel).map((skill) => skill.id);
}

export function primarySkill(level) {
  const ids = unlockedSkills(level);
  return ids.length > 0 ? ids[ids.length - 1] : null;
}

/** Tambah XP, naikkan level bila cukup. Mengubah state, hasil = daftar level baru. */
export function grantXp(state, amount) {
  const levels = [];
  state.player.xp += Math.max(0, Math.round(amount));
  while (state.player.level < 99 && state.player.xp >= xpNeeded(state.player.level)) {
    state.player.xp -= xpNeeded(state.player.level);
    state.player.level += 1;
    state.player.maxHp = maxHpFor(state.player.level);
    state.player.hp = state.player.maxHp;
    state.player.skills = unlockedSkills(state.player.level);
    if (state.player.skills.includes("aura_slash")) {
      state.player.skillLevel = Math.max(1, Math.min(3, Math.floor((state.player.level - 1) / 4) + 1));
    }
    levels.push(state.player.level);
  }
  return levels;
}

// --------------------------------------------------------------------------- //
// state
// --------------------------------------------------------------------------- //
export function zoneById(id) {
  return ZONES.find((z) => z.id === id) ?? ZONES[0];
}

export function createState(profileId, name, zoneId = "greenwood") {
  const zone = zoneById(zoneId);
  return {
    version: SAVE_VERSION,
    profileId,
    profileName: name,
    player: {
      x: zone.checkpoint.x,
      y: zone.groundY,
      facing: 1,
      hp: maxHpFor(1),
      maxHp: maxHpFor(1),
      level: 1,
      xp: 0,
      skillLevel: 0,
      skills: [],
    },
    world: {
      zone: zone.id,
      checkpoint: zone.checkpoint.id,
      flags: {},
      defeated: {},
    },
    stats: { playtime: 0, deaths: 0, kills: 0 },
  };
}

/** Terima payload dari server / localStorage, rapikan supaya aman dipakai. */
export function normalizeState(raw, fallbackZone = "greenwood") {
  const zone = zoneById(raw?.world?.zone ?? fallbackZone);
  const level = clampInt(raw?.player?.level, 1, 99, 1);
  const player = {
    x: clampNumber(raw?.player?.x, -10000, 1000000, zone.checkpoint.x),
    y: clampNumber(raw?.player?.y, -10000, 100000, zone.groundY),
    facing: raw?.player?.facing === -1 ? -1 : 1,
    level,
    maxHp: clampInt(raw?.player?.maxHp, 10, 9999, maxHpFor(level)),
    hp: 0,
    xp: clampInt(raw?.player?.xp, 0, 10000000, 0),
    skillLevel: clampInt(raw?.player?.skillLevel, 0, 3, 0),
    skills: [],
  };
  player.hp = clampInt(raw?.player?.hp, 0, player.maxHp, player.maxHp);
  player.skills = Array.isArray(raw?.player?.skills)
    ? raw.player.skills.filter((s) => SKILLS.some((k) => k.id === s))
    : unlockedSkills(level);

  const flags = {};
  for (const [key, value] of Object.entries(raw?.world?.flags ?? {})) {
    if (/^[\w.:-]{1,60}$/.test(key)) flags[key] = value === true;
  }
  const defeated = {};
  for (const [key, value] of Object.entries(raw?.world?.defeated ?? {})) {
    if (/^[\w.:-]{1,60}$/.test(key)) defeated[key] = clampInt(value, 0, 1000000, 0);
  }

  return {
    version: SAVE_VERSION,
    profileId: raw?.profileId ?? null,
    profileName: raw?.profileName ?? "",
    player,
    world: {
      zone: zone.id,
      checkpoint: typeof raw?.world?.checkpoint === "string" ? raw.world.checkpoint : zone.checkpoint.id,
      flags,
      defeated,
    },
    stats: {
      playtime: Math.max(0, Number(raw?.stats?.playtime) || 0),
      deaths: clampInt(raw?.stats?.deaths, 0, 1000000, 0),
      kills: clampInt(raw?.stats?.kills, 0, 1000000, 0),
    },
  };
}

/** Bentuk yang dikirim ke backend (tanpa field internal). */
export function serializeState(state, playtime) {
  const payload = {
    version: SAVE_VERSION,
    player: {
      x: Math.round(state.player.x * 100) / 100,
      y: Math.round(state.player.y * 100) / 100,
      facing: state.player.facing,
      hp: Math.round(state.player.hp),
      maxHp: Math.round(state.player.maxHp),
      level: state.player.level,
      xp: Math.round(state.player.xp),
      skillLevel: state.player.skillLevel,
      skills: [...state.player.skills],
    },
    world: {
      zone: state.world.zone,
      checkpoint: state.world.checkpoint,
      flags: { ...state.world.flags },
      defeated: { ...state.world.defeated },
    },
    stats: {
      playtime: Math.round(playtime ?? state.stats.playtime),
      deaths: state.stats.deaths,
      kills: state.stats.kills,
    },
  };
  return payload;
}

function clampInt(value, min, max, fallback) {
  const n = Number(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.max(min, Math.min(max, Math.round(n)));
}

function clampNumber(value, min, max, fallback) {
  const n = Number(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.max(min, Math.min(max, n));
}

// --------------------------------------------------------------------------- //
// runtime + simulasi
// --------------------------------------------------------------------------- //
export function createRuntime(state) {
  const zone = zoneById(state.world.zone);
  return {
    state,
    zone,
    input: createInput(),
    // satu sumber kebenaran: field pemain di objek state + field khusus runtime
    player: Object.assign(state.player, {
      vx: 0,
      vy: 0,
      onGround: true,
      anim: "idle",
      animTime: 0,
      animLocked: false,
      attackTimer: 0,
      skillTimer: 0,
      invuln: 0,
      hurtTimer: 0,
      dead: false,
      deadTimer: 0,
      energy: 100,
      jumpBufferTimer: 0,
      coyoteTimer: 0,
      takeoffTimer: 0,
      flash: 0,
    }),
    enemies: spawnEnemies(state, zone, false),
    effects: [],
    attackId: 0,
    camera: { x: 0, y: 0 },
    time: 0,
    playtime: state.stats.playtime,
    zoneIntro: { name: zone.name, time: 2.4 },
    pendingEvents: [],
  };
}

export function createInput() {
  return { left: false, right: false, run: false, jump: false, attack: false, skill: false, aim: 1 };
}

export function spawnEnemies(state, zone, respawnDefeated) {
  return zone.enemies
    .filter((spawn) => respawnDefeated || !isSpawnDefeated(state, spawn))
    .map((spawn, index) => {
      const spec = ENEMY_TYPES[spawn.type];
      return {
        id: `${zone.id}_${spawn.type}_${index}`,
        type: spawn.type,
        name: spec.name,
        boss: Boolean(spec.boss),
        bossFlag: spawn.bossFlag ?? null,
        x: spawn.x,
        y: spawn.y ?? zone.groundY,
        homeX: spawn.x,
        homeY: spawn.y ?? zone.groundY,
        patrol: spawn.patrol ? [...spawn.patrol] : [spawn.x - (spec.patrolRange ?? 80), spawn.x + (spec.patrolRange ?? 80)],
        vx: 0,
        vy: 0,
        dir: -1,
        hp: spec.hp,
        maxHp: spec.hp,
        hurtTimer: 0,
        dead: false,
        deadTimer: 0,
        phase: 0,
        anim: 0,
        width: spec.width,
        height: spec.height,
      };
    });
}

/** Musuh biasa hidup lagi saat zona dimuat ulang; boss yang sudah kalah tetap hilang. */
function isSpawnDefeated(state, spawn) {
  return spawn.bossFlag ? state.world.flags[spawn.bossFlag] === true : false;
}

/** Simulasi satu langkah. Mengubah runtime, mengembalikan daftar event. */
export function simulate(rt, input, dt) {
  dt = Math.min(dt, 1 / 30);
  rt.time += dt;
  rt.playtime += dt;
  if (rt.zoneIntro.time > 0) rt.zoneIntro.time -= dt;

  const events = [];
  const p = rt.player;
  const zone = rt.zone;

  if (p.dead) {
    p.deadTimer += dt;
    p.vy += PHYSICS.gravity * dt;
    p.y = Math.min(zone.groundY, p.y + p.vy * dt);
    anim(rt, "death");
    if (p.deadTimer > 1.6) {
      respawn(rt, events);
    }
    stepEnemies(rt, dt, events, false);
    return events;
  }

  // ---- input horizontal
  const speed = input.run ? PHYSICS.runSpeed : PHYSICS.walkSpeed;
  let target = 0;
  if (input.left) target -= speed;
  if (input.right) target += speed;
  if (p.hurtTimer > 0) target = 0;
  p.vx = target;
  if (target !== 0) p.facing = Math.sign(target);

  // ---- lompat
  if (input.jump) p.jumpBufferTimer = PHYSICS.jumpBuffer;
  else p.jumpBufferTimer = 0;
  if (p.onGround) p.coyoteTimer = PHYSICS.coyoteTime;
  else p.coyoteTimer = Math.max(0, p.coyoteTimer - dt);

  // tahan pose crouch (jump_1) sebentar sebelum benar-benar meluncur ke atas
  if (p.takeoffTimer > 0) {
    p.takeoffTimer = Math.max(0, p.takeoffTimer - dt);
    if (p.takeoffTimer === 0) {
      p.vy = PHYSICS.jumpVelocity;
      p.onGround = false;
      p.coyoteTimer = 0;
      events.push({ type: "jump" });
    }
  } else if (p.jumpBufferTimer > 0 && p.coyoteTimer > 0 && !p.animLocked && p.hurtTimer <= 0) {
    p.takeoffTimer = 0.09;
    p.jumpBufferTimer = 0;
    anim(rt, "jump_takeoff");
  }

  // ---- combat
  if (input.attack && p.attackTimer <= 0 && p.skillTimer <= 0 && !p.animLocked && p.hurtTimer <= 0) {
    p.attackTimer = COMBAT.attackDuration;
    rt.attackId += 1;
    anim(rt, "attack", true);
    events.push({ type: "attack" });
  }
  const skillId = primarySkill(state_level(rt));
  if (input.skill && skillId && p.skillTimer <= 0 && p.attackTimer <= 0 && !p.animLocked && p.hurtTimer <= 0) {
    if (p.energy >= COMBAT.skillCost) {
      p.energy -= COMBAT.skillCost;
      p.skillTimer = COMBAT.skillDuration;
      anim(rt, "skill", true);
      events.push({ type: "skill", id: skillId });
      resolveSkill(rt, events);
    } else {
      events.push({ type: "skill_no_energy" });
    }
  }
  if (p.energy < 100) p.energy = Math.min(100, p.energy + COMBAT.skillRegen * dt);

  if (p.attackTimer > 0) {
    const elapsed = COMBAT.attackDuration - p.attackTimer;
    if (elapsed >= COMBAT.attackActiveFrom && elapsed <= COMBAT.attackActiveTo) {
      resolveAttack(rt, events);
    }
    p.attackTimer = Math.max(0, p.attackTimer - dt);
    if (p.attackTimer === 0 && p.anim === "attack") p.animLocked = false;
  }
  if (p.skillTimer > 0) {
    p.skillTimer = Math.max(0, p.skillTimer - dt);
    if (p.skillTimer === 0 && p.anim === "skill") p.animLocked = false;
  }
  if (p.invuln > 0) p.invuln = Math.max(0, p.invuln - dt);
  if (p.hurtTimer > 0) p.hurtTimer = Math.max(0, p.hurtTimer - dt);
  if (p.flash > 0) p.flash = Math.max(0, p.flash - dt);

  // ---- fisika vertikal + tabrakan
  if (p.takeoffTimer > 0) p.vy = 0;
  else p.vy = Math.min(PHYSICS.maxFall, p.vy + PHYSICS.gravity * dt);
  const previousBottom = p.y;
  p.x = Math.max(12, Math.min(zone.width - 12, p.x + p.vx * dt));
  p.y += p.vy * dt;
  p.onGround = false;

  if (p.y >= zone.groundY) {
    p.y = zone.groundY;
    p.vy = 0;
    p.onGround = true;
  } else if (p.vy > 0) {
    for (const platform of zone.platforms) {
      const withinX = p.x + PHYSICS.bodyWidth / 2 > platform.x && p.x - PHYSICS.bodyWidth / 2 < platform.x + platform.w;
      if (withinX && previousBottom <= platform.y && p.y >= platform.y) {
        p.y = platform.y;
        p.vy = 0;
        p.onGround = true;
        break;
      }
    }
  }

  // ---- animasi lokomotif
  if (p.hurtTimer > 0) {
    p.anim = "hurt";
    p.animLocked = false;
  } else if (!p.animLocked) {
    if (p.takeoffTimer > 0) p.anim = "jump_takeoff";
    else if (!p.onGround) p.anim = p.vy < 0 ? "jump_up" : "jump_down";
    else if (Math.abs(p.vx) > 4) p.anim = input.run ? "run" : "walk";
    else p.anim = "idle";
    p.animTime += dt;
  }

  // ---- checkpoint, gerbang, event runtime
  const checkpoint = zone.checkpoint;
  if (Math.abs(p.x - checkpoint.x) < 40) {
    if (rt.state.world.checkpoint !== checkpoint.id) {
      rt.state.world.checkpoint = checkpoint.id;
      rt.pendingEvents.push({ type: "checkpoint", id: checkpoint.id });
      events.push({ type: "checkpoint", id: checkpoint.id, name: checkpoint.name });
    }
    p.hp = Math.min(p.maxHp, p.hp + 45 * dt);
  }
  for (const gate of zone.gates) {
    if (p.x > gate.x) events.push({ type: "gate", to: gate.to, label: gate.label });
  }

  stepEnemies(rt, dt, events, true);
  updateEffects(rt, dt);
  return events;
}

function state_level(rt) {
  return rt.player.level;
}

function anim(rt, name, lock = false) {
  const p = rt.player;
  p.anim = name;
  p.animVariant = null;
  p.animLocked = lock;
  p.animTime = 0;
}

/** Pindah zona: simpan progres, spawn musuh zona baru. */
export function travelTo(rt, zoneId, fromX = null) {
  const zone = zoneById(zoneId);
  rt.zone = zone;
  rt.state.world.zone = zone.id;
  if (zoneId !== "greenwood" && !zoneById(zoneId).gates.length) {
    // zona terakhir
  }
  rt.player.x = fromX ?? zone.checkpoint.x;
  rt.player.y = zone.groundY;
  rt.player.vx = 0;
  rt.player.vy = 0;
  rt.enemies = spawnEnemies(rt.state, zone, false);
  rt.effects = [];
  rt.zoneIntro = { name: zone.name, time: 2.4 };
  rt.camera.x = rt.player.x - 200;
  return zone;
}

/** Musuh dibangkitkan ulang saat pemain kembali ke zona (kecuali boss yang sudah kalah). */
export function respawnEnemies(rt) {
  rt.enemies = spawnEnemies(rt.state, rt.zone, false);
}

function respawn(rt, events) {
  const zone = rt.zone;
  const p = rt.player;
  rt.state.stats.deaths += 1;
  p.dead = false;
  p.deadTimer = 0;
  p.hp = p.maxHp;
  p.energy = 100;
  p.x = zone.checkpoint.x;
  p.y = zone.groundY;
  p.vx = 0;
  p.vy = 0;
  p.invuln = 1.5;
  p.flash = 0;
  anim(rt, "idle");
  respawnEnemies(rt);
  rt.state.player.x = p.x;
  rt.state.player.y = p.y;
  events.push({ type: "respawn", x: p.x, y: p.y, checkpoint: zone.checkpoint.id });
}

function attackBox(rt) {
  const p = rt.player;
  const reach = COMBAT.attackReach;
  const x = p.facing > 0 ? p.x + 6 : p.x - 6 - reach;
  return { x0: x, x1: x + reach, y0: p.y - PHYSICS.bodyHeight, y1: p.y + 4 };
}

function resolveAttack(rt, events) {
  const box = attackBox(rt);
  const damage = attackDamageOf(rt.player.level);
  for (const enemy of rt.enemies) {
    if (enemy.dead || enemy.hitBy === rt.attackId) continue;
    const ex0 = enemy.x - enemy.width / 2;
    const ex1 = enemy.x + enemy.width / 2;
    const ey0 = enemy.y - enemy.height;
    const ey1 = enemy.y;
    if (ex1 < box.x0 || ex0 > box.x1 || ey1 < box.y0 || ey0 > box.y1) continue;
    enemy.hitBy = rt.attackId;
    hitEnemy(rt, enemy, damage, rt.player.facing, events, "attack");
  }
}

function resolveSkill(rt, events) {
  const p = rt.player;
  const damage = skillDamageOf(p.level, Math.max(1, p.skillLevel));
  rt.effects.push({ type: "aura", x: p.x, y: p.y - 20, radius: COMBAT.skillRadius, life: 0.45, maxLife: 0.45 });
  for (const enemy of rt.enemies) {
    if (enemy.dead) continue;
    const dx = enemy.x - p.x;
    const dy = enemy.y - enemy.height / 2 - p.y;
    if (Math.hypot(dx, dy) <= COMBAT.skillRadius) {
      hitEnemy(rt, enemy, damage, Math.sign(dx) || p.facing, events, "skill");
    }
  }
}

function hitEnemy(rt, enemy, damage, direction, events, source) {
  enemy.hp -= damage;
  enemy.hurtTimer = 0.18;
  enemy.vx = (direction || 1) * COMBAT.knockback * (enemy.boss ? 0.25 : 1);
  events.push({ type: "hit", enemy: enemy.id, damage, source, x: enemy.x, y: enemy.y - enemy.height / 2 });
  if (enemy.hp <= 0 && !enemy.dead) {
    enemy.dead = true;
    enemy.deadTimer = 0;
    rt.state.stats.kills += 1;
    const key = enemy.type;
    rt.state.world.defeated[key] = (rt.state.world.defeated[key] ?? 0) + 1;
    const levels = grantXp(rt.state, ENEMY_TYPES[enemy.type].xp);
    rt.player.level = rt.state.player.level;
    rt.player.maxHp = rt.state.player.maxHp;
    rt.player.hp = Math.min(rt.state.player.hp, rt.player.maxHp);
    events.push({ type: "kill", enemy: enemy.id, name: enemy.name, xp: ENEMY_TYPES[enemy.type].xp });
    for (const level of levels) {
      events.push({ type: "level_up", level });
      rt.effects.push({ type: "levelup", x: rt.player.x, y: rt.player.y - 40, life: 1, maxLife: 1 });
    }
    if (enemy.boss && enemy.bossFlag) {
      rt.state.world.flags[enemy.bossFlag] = true;
      events.push({ type: "boss_defeated", flag: enemy.bossFlag, name: enemy.name });
    }
  }
}

function damagePlayer(rt, amount, fromX, events) {
  const p = rt.player;
  if (p.invuln > 0 || p.dead) return;
  p.hp -= amount;
  p.invuln = COMBAT.invulnTime;
  p.hurtTimer = COMBAT.hurtStun;
  p.flash = 0.25;
  p.vx = (p.x < fromX ? -1 : 1) * 90;
  anim(rt, "hurt");
  p.animLocked = false;
  events.push({ type: "player_hurt", amount, hp: Math.max(0, Math.round(p.hp)) });
  if (p.hp <= 0) {
    p.hp = 0;
    p.dead = true;
    p.deadTimer = 0;
    p.vy = -260;
    anim(rt, "death");
    events.push({ type: "player_died" });
  }
}

function stepEnemies(rt, dt, events, allowDamage) {
  const p = rt.player;
  for (const enemy of rt.enemies) {
    if (enemy.dead) {
      enemy.deadTimer += dt;
      continue;
    }
    if (enemy.hurtTimer > 0) enemy.hurtTimer = Math.max(0, enemy.hurtTimer - dt);
    const spec = ENEMY_TYPES[enemy.type];
    enemy.anim += dt;

    if (enemy.type === "bat") {
      const dist = p.x - enemy.x;
      if (Math.abs(dist) < spec.chaseRange && !p.dead) {
        enemy.dir = Math.sign(dist) || enemy.dir;
        enemy.x += enemy.dir * spec.speed * dt;
        const targetY = p.y - PHYSICS.bodyHeight - 6;
        enemy.y += Math.sign(targetY - enemy.y) * Math.min(48, Math.abs(targetY - enemy.y)) * dt;
      } else {
        enemy.x += enemy.dir * spec.speed * 0.4 * dt;
        enemy.y = enemy.homeY + Math.sin(enemy.anim * 2.4) * 10;
        if (enemy.x < enemy.homeX - 90 || enemy.x > enemy.homeX + 90) enemy.dir *= -1;
      }
    } else if (enemy.type === "warden") {
      const dist = p.x - enemy.x;
      enemy.dir = Math.sign(dist) || enemy.dir;
      if (Math.abs(dist) < 420) {
        enemy.x += enemy.dir * spec.speed * dt;
        if (Math.abs(dist) < 96 && enemy.anim > 2.4) {
          enemy.anim = 0;
          if (allowDamage) damagePlayer(rt, spec.contact, enemy.x, events);
        }
      }
    } else {
      enemy.x += enemy.dir * spec.speed * dt;
      if (enemy.x < enemy.patrol[0]) enemy.dir = 1;
      if (enemy.x > enemy.patrol[1]) enemy.dir = -1;
      enemy.y = enemy.homeY - Math.abs(Math.sin(enemy.anim * 3)) * 6;
    }

    if (enemy.vx !== 0) {
      enemy.x += enemy.vx * dt;
      enemy.vx *= enemy.hurtTimer > 0 ? 0.86 : 0.2;
      if (Math.abs(enemy.vx) < 4) enemy.vx = 0;
    }

    if (allowDamage && !p.dead) {
      const ex0 = enemy.x - enemy.width / 2;
      const ex1 = enemy.x + enemy.width / 2;
      const ey0 = enemy.y - enemy.height;
      const ey1 = enemy.y;
      const px0 = p.x - PHYSICS.bodyWidth / 2;
      const px1 = p.x + PHYSICS.bodyWidth / 2;
      const py0 = p.y - PHYSICS.bodyHeight;
      const py1 = p.y;
      const overlap = ex1 > px0 && ex0 < px1 && ey1 > py0 + 6 && ey0 < py1;
      if (overlap && enemy.type !== "warden") damagePlayer(rt, spec.contact, enemy.x, events);
    }
  }
}

function updateEffects(rt, dt) {
  for (const effect of rt.effects) effect.life -= dt;
  rt.effects = rt.effects.filter((effect) => effect.life > 0);
}

/** Salin nilai runtime ke state, lalu bentuk payload yang dikirim ke backend. */
export function snapshot(rt) {
  const state = rt.state;
  // rt.player adalah objek yang sama dengan state.player, jadi cukup playtime yang perlu disalin
  state.player.x = rt.player.x;
  state.player.y = rt.player.y;
  state.player.facing = rt.player.facing;
  state.player.hp = Math.max(0, Math.round(rt.player.hp));
  state.stats.playtime = Math.round(rt.playtime);
  return serializeState(state, rt.playtime);
}

/** Total XP yang dibutuhkan untuk naik ke level berikutnya. */
export function progressInfo(state) {
  const need = xpNeeded(state.player.level);
  return {
    level: state.player.level,
    xp: state.player.xp,
    need,
    ratio: Math.max(0, Math.min(1, state.player.xp / need)),
    kills: state.stats.kills,
    deaths: state.stats.deaths,
    playtime: state.stats.playtime,
    skillUnlocked: state.player.level >= COMBAT.skillUnlockLevel,
    skillLevel: state.player.skillLevel,
  };
}
