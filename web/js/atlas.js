/**
 * atlas.js — memuat spritesheet hasil tools/make_spritesheet.py dan memutar animasinya.
 *
 * Region tiap frame = satu cell penuh (88x96) supaya semua frame berukuran sama,
 * jadi tidak ada jitter. Titik kaki (anchor) dibaca dari assets/character/generated/anchor.json.
 */

export const STATE_TO_ANIMATION = {
  idle: { animation: "idle" },
  walk: { animation: "walk" },
  run: { animation: "run" },
  jump_takeoff: { animation: "jump", index: 0 },
  jump_up: { animation: "jump", index: 1 },
  jump_down: { animation: "jump", index: 2 },
  attack: { animation: "attack" },
  skill: { animation: "skill" },
  hurt: { animation: "hurt" },
  death: { animation: "death" },
};

export const ONE_SHOT_STATES = new Set(["attack", "skill", "hurt", "death"]);
export const ENEMY_ANIMATIONS = { idle: "idle", hurt: "hurt", death: "death" };

export class SpriteSheet {
  constructor(image, atlas, anchor) {
    this.image = image;
    this.atlas = atlas;
    this.anchor = anchor;
    this.animations = new Map(atlas.animations.map((a) => [a.name, a]));
    this.frames = new Map(atlas.frames.map((f) => [f.name, f.rect]));
    this.cell = atlas.cell;
    this.foot = anchor?.foot ?? { x: Math.round(this.cell.w / 2), y: this.cell.h - 1 };
  }

  static async load(base = "/assets/character") {
    const [atlas, anchor, image] = await Promise.all([
      fetch(`${base}/build/arga_spritesheet.json`).then(checkOk).then((r) => r.json()),
      fetch(`${base}/generated/anchor.json`).then(checkOk).then((r) => r.json()).catch(() => null),
      loadImage(`${base}/build/arga_spritesheet.png`),
    ]);
    return new SpriteSheet(image, atlas, anchor);
  }

  has(name) {
    return this.frames.has(name);
  }

  animation(name) {
    return this.animations.get(name) ?? this.animations.get("idle");
  }

  draw(ctx, frameName, footX, footY, facing = 1) {
    const rect = this.frames.get(frameName);
    if (!rect) return;
    const dx = Math.round(footX - this.foot.x);
    const dy = Math.round(footY - this.foot.y);
    if (facing < 0) {
      ctx.save();
      ctx.translate(Math.round(footX), 0);
      ctx.scale(-1, 1);
      ctx.translate(-Math.round(footX), 0);
      ctx.drawImage(this.image, rect.x, rect.y, rect.w, rect.h, dx, dy, rect.w, rect.h);
      ctx.restore();
    } else {
      ctx.drawImage(this.image, rect.x, rect.y, rect.w, rect.h, dx, dy, rect.w, rect.h);
    }
  }

  drawScaled(ctx, frameName, x, y, scale, facing = 1) {
    const rect = this.frames.get(frameName);
    if (!rect) return;
    ctx.save();
    ctx.translate(x, y);
    if (facing < 0) ctx.scale(-1, 1);
    ctx.scale(scale, scale);
    ctx.drawImage(this.image, rect.x, rect.y, rect.w, rect.h, -this.foot.x, -this.foot.y, rect.w, rect.h);
    ctx.restore();
  }
}

/** Memutar animasi untuk satu state game (idle/walk/run/jump_up/attack/...). */
export class Animator {
  constructor(sheet) {
    this.sheet = sheet;
    this.state = "idle";
    this.time = 0;
    this.frameName = null;
  }

  setState(state) {
    if (this.state === state) return;
    this.state = state;
    this.time = 0;
  }

  update(dt) {
    const spec = STATE_TO_ANIMATION[this.state] ?? STATE_TO_ANIMATION.idle;
    const animation = this.sheet.animation(spec.animation);
    const frames = animation?.frames ?? [];
    if (frames.length === 0) {
      this.frameName = "idle_1";
      return this.frameName;
    }
    if (typeof spec.index === "number") {
      this.frameName = frames[Math.min(spec.index, frames.length - 1)];
      return this.frameName;
    }
    this.time += dt;
    const fps = animation.fps || 8;
    const index = ONE_SHOT_STATES.has(this.state)
      ? Math.min(frames.length - 1, Math.floor(this.time * fps))
      : Math.floor(this.time * fps) % frames.length;
    this.frameName = frames[index];
    return this.frameName;
  }

  reset(state = "idle") {
    this.state = state;
    this.time = 0;
  }
}

function checkOk(response) {
  if (!response.ok) throw new Error(`${response.url} -> HTTP ${response.status}`);
  return response;
}

function loadImage(src) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error(`gagal memuat gambar ${src}`));
    image.src = src;
  });
}
