using System;
using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>
/// Partikel ringan dengan pooling: sprite dibuat sekali saat startup, dipakai
/// ulang (tidak ada alokasi node saat bermain). Semua murni visual.
/// </summary>
public sealed partial class ParticlePool : Node2D
{
    private sealed class Particle
    {
        public required Sprite2D Sprite;
        public Vector2 Vel;
        public float Age, Life, Gravity, Drag, Spin;
        public float SwayAmp, SwayFreq, SwayPhase, ScaleFrom, ScaleTo, Alpha;
        public bool Active;
    }

    private readonly List<Particle> _particles = new();
    private readonly List<Particle> _active = new();

    public int ActiveCount => _active.Count;
    public int Capacity => _particles.Count;

    public ParticlePool(string name, Texture2D texture, int capacity)
    {
        Name = name;
        YSortEnabled = false;
        for (var i = 0; i < capacity; i++)
        {
            var sprite = new Sprite2D
            {
                Name = $"P{i}",
                Texture = texture,
                Centered = true,
                Visible = false,
                ZIndex = 5,
            };
            AddChild(sprite);
            _particles.Add(new Particle { Sprite = sprite });
        }
    }

    /// <summary>Ambil satu sprite dari pool lalu hidupkan; false kalau pool penuh.</summary>
    public bool Spawn(Vector2 position, Vector2 velocity, float life, Color color,
        float scaleFrom, float scaleTo, float gravity, float drag, float spin,
        float swayAmp, float swayFreq, float swayPhase)
    {
        Particle? free = null;
        for (var i = 0; i < _particles.Count; i++)
        {
            if (_particles[i].Active) continue;
            free = _particles[i];
            break;
        }
        if (free is null) return false;

        free.Active = true;
        free.Age = 0f;
        free.Life = MathF.Max(0.05f, life);
        free.Vel = velocity;
        free.Gravity = gravity;
        free.Drag = drag;
        free.Spin = spin;
        free.SwayAmp = swayAmp;
        free.SwayFreq = swayFreq;
        free.SwayPhase = swayPhase;
        free.ScaleFrom = scaleFrom;
        free.ScaleTo = scaleTo;
        free.Alpha = color.A;
        free.Sprite.Position = position;
        free.Sprite.Rotation = 0f;
        free.Sprite.Scale = new Vector2(scaleFrom, scaleFrom);
        free.Sprite.Modulate = color;
        free.Sprite.Visible = true;
        _active.Add(free);
        return true;
    }

    /// <summary>Majukan semua partikel; yang habis umurnya dikembalikan ke pool.</summary>
    public void Advance(double delta, Vector2 wind)
    {
        var dt = (float)delta;
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var p = _active[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                p.Active = false;
                p.Sprite.Visible = false;
                _active.RemoveAt(i);
                continue;
            }
            var t = p.Age / p.Life;
            p.Vel += new Vector2(wind.X * p.Drag, p.Gravity) * dt;
            var sway = MathF.Sin(p.Age * p.SwayFreq + p.SwayPhase) * p.SwayAmp;
            p.Sprite.Position += (p.Vel + new Vector2(sway, 0f)) * dt;
            p.Sprite.Rotation += p.Spin * dt;
            var scale = ScaleFromTo(p, t);
            p.Sprite.Scale = new Vector2(scale, scale);
            var color = p.Sprite.Modulate;
            color.A = p.Alpha * (t < 0.15f ? t / 0.15f : 1f - Smoothstep(0.55f, 1f, t));
            p.Sprite.Modulate = color;
        }
    }

    private static float ScaleFromTo(Particle p, float t) =>
        p.ScaleFrom + (p.ScaleTo - p.ScaleFrom) * t;

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>
/// Ambience api & pemulihan hutan (tahap 2c) — murni dekoratif:
///   · nyala api 4 frame (AnimatedSprite2D) di setiap titik api
///   · bara (ember) yang naik dari api, terbawa angin lembut
///   · abu beterbangan di zona bekas terbakar + asap menebal kalau kamera di sana
///   · tunas hijau tumbuh lalu memudar di zona bekas terbakar (hutan pulih)
/// Tidak menyentuh Rules, damage, kolisi, atau AI.
/// </summary>
public sealed partial class FireAmbience : Node2D
{
    public const int EmberCapacity = 160;
    public const int AshCapacity = 140;
    public const int MaxSprouts = 12;
    /// <summary>Tambahan AshDensity Fx saat kamera berada di zona terbakar.</summary>
    public const float BurntAshBoost = 0.15f;

    public ParticlePool? Embers { get; private set; }
    public ParticlePool? Ash { get; private set; }
    public readonly List<AnimatedSprite2D> Flames = new();
    public int SproutCount => _activeSprouts.Count;
    public float AshBoost { get; private set; }

    private readonly List<(Vector2 Pos, float Phase, float Size)> _fires = new();
    private readonly List<Rect2> _burntRects = new();
    private readonly List<Sprout> _sprouts = new();
    private readonly List<Sprout> _activeSprouts = new();
    private World? _world;
    private Vector2 _cam;
    private double _emberTimer, _ashTimer, _sproutTimer;
    private int _nextFire, _nextBurnt;
    private uint _state = 0x1234ABCDu;

    private static readonly Vector2 ViewSize = new(480, 270);

    private sealed class Sprout
    {
        public required Sprite2D Sprite;
        public double Age;
        public float Phase, Flip;
    }

    public void Configure(World world)
    {
        _world = world;
        _fires.AddRange(world.HeatSources);
        foreach (var zone in world.Data.BurntZones)
            _burntRects.Add(zone.RectPx(World.TileSize));

        // nyala api animasi: 4 frame 16x16, ujung bawah di posisi titik api
        var flameTexture = Assets.LoadTexture(Assets.FxFlamePng);
        var frames = new SpriteFrames();
        frames.AddAnimation("burn");
        frames.SetAnimationSpeed("burn", 9);
        frames.SetAnimationLoop("burn", true);
        for (var i = 0; i < 4; i++)
        {
            frames.AddFrame("burn", new AtlasTexture
            {
                Atlas = flameTexture,
                Region = new Rect2(i * 16, 0, 16, 16),
            });
        }
        foreach (var (pos, _phase, size) in _fires)
        {
            var flame = new AnimatedSprite2D
            {
                Name = $"Burn_{Flames.Count}",
                SpriteFrames = frames,
                Animation = "burn",
                Autoplay = "burn",
                Position = pos,
                Scale = Vector2.One * (size / 44f),
                Offset = new Vector2(0, 8),
            };
            flame.Play("burn");
            AddChild(flame);
            Flames.Add(flame);
        }

        Embers = new ParticlePool("Embers", Assets.LoadTexture(Assets.FxGlowSoftPng), EmberCapacity);
        Ash = new ParticlePool("Ash", Assets.LoadTexture(Assets.FxGlowSoftPng), AshCapacity);
        AddChild(Embers);
        AddChild(Ash);

        var sproutTexture = Assets.LoadTexture(Assets.FxSproutPng);
        for (var i = 0; i < MaxSprouts; i++)
        {
            var sprite = new Sprite2D
            {
                Name = $"Sprout_{i}",
                Texture = sproutTexture,
                Centered = false,
                Offset = new Vector2(-4, -8),
                Visible = false,
                ZIndex = 4,
            };
            AddChild(sprite);
            _sprouts.Add(new Sprout { Sprite = sprite });
        }
    }

    public void SetCameraCenter(Vector2 center) => _cam = center;

    /// <summary>True kalau titik (piksel dunia) ada di salah satu zona bekas terbakar.</summary>
    public bool InBurntZone(Vector2 point)
    {
        foreach (var rect in _burntRects)
            if (rect.HasPoint(point))
                return true;
        return false;
    }

    /// <summary>Paksa satu tunas tumbuh (dipakai tes); false kalau semua slot terpakai.</summary>
    public Sprite2D? ForceSproutAt(Vector2 feetPosition)
    {
        var sprout = AcquireSprout();
        if (sprout is null) return null;
        sprout.Sprite.Position = feetPosition;
        return sprout.Sprite;
    }

    /// <summary>Paksa satu bara muncul (dipakai tes); false kalau pool penuh.</summary>
    public bool ForceEmber(Vector2 position) =>
        Embers?.Spawn(position, new Vector2(0f, -24f), 1.2f, new Color(1f, 0.72f, 0.36f, 0.9f),
            0.08f, 0.02f, 14f, 0.6f, 0f, 8f, 3f, 0f) ?? false;

    public void Advance(double delta)
    {
        var seconds = Time.GetTicksMsec() / 1000f;
        // angin lembut naik-turun (bara & abu mengikuti)
        var wind = new Vector2(10f + 6f * MathF.Sin(seconds * 0.23f), 0f);

        AshBoost = InBurntZone(_cam) ? BurntAshBoost : 0f;

        var view = new Rect2(_cam - ViewSize * 0.5f - new Vector2(48, 48), ViewSize + new Vector2(96, 96));

        // bara: keluar bergiliran dari api yang terlihat kamera
        _emberTimer += delta;
        while (_emberTimer >= 0.14 && _fires.Count > 0 && Embers is not null)
        {
            _emberTimer -= 0.14;
            var (pos, phase, _size) = _fires[_nextFire % _fires.Count];
            _nextFire++;
            if (!view.HasPoint(pos)) continue;
            Embers.Spawn(pos + new Vector2(Rand(-6f, 6f), Rand(-2f, 2f)),
                new Vector2(Rand(-9f, 9f), Rand(-34f, -18f)),
                Rand(1.1f, 2.0f), new Color(1f, 0.72f, 0.36f, 0.9f),
                Rand(0.05f, 0.09f), 0.015f, 14f, 0.6f, 0f,
                Rand(6f, 14f), Rand(2f, 4f), phase);
        }

        // abu: hanya dari zona terbakar yang irisannya terlihat kamera
        _ashTimer += delta;
        while (_ashTimer >= 0.09 && Ash is not null && _burntRects.Count > 0)
        {
            _ashTimer -= 0.09;
            var rect = _burntRects[_nextBurnt % _burntRects.Count];
            _nextBurnt++;
            var clip = rect.Intersection(view);
            if (clip.Size.X < 2f || clip.Size.Y < 2f) continue;
            Ash.Spawn(new Vector2(Rand(clip.Position.X, clip.End.X), clip.Position.Y + Rand(0f, 12f)),
                new Vector2(Rand(-4f, 4f), Rand(6f, 12f)),
                Rand(2.2f, 3.6f), new Color(0.62f, 0.60f, 0.56f, 0.75f),
                Rand(0.035f, 0.055f), 0.02f, 4f, 0.5f, Rand(-0.6f, 0.6f),
                Rand(4f, 9f), Rand(1.5f, 3f), Rand(0f, 6.28f));
        }

        // tunas: tumbuh perlahan di zona terbakar (hutan pulih)
        _sproutTimer += delta;
        if (_sproutTimer >= 1.6 && _world is not null)
        {
            _sproutTimer = 0;
            if (_activeSprouts.Count < MaxSprouts)
                TrySpawnSprout(view);
        }
        UpdateSprouts(delta);

        Embers?.Advance(delta, wind);
        Ash?.Advance(delta, wind);
    }

    private void TrySpawnSprout(Rect2 view)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            if (_burntRects.Count == 0) return;
            var rect = _burntRects[(int)(_nextBurnt++ % _burntRects.Count)];
            var clip = rect.Intersection(view);
            if (clip.Size.X < 16f || clip.Size.Y < 16f) continue;
            var px = Rand(clip.Position.X, clip.End.X - 8f);
            var py = Rand(clip.Position.Y, clip.End.Y - 16f);
            if (_world is null || !_world.Grid.WalkableAt(new Vector2(px + 8f, py + 16f))) continue;
            var sprout = AcquireSprout();
            if (sprout is null) return;
            sprout.Sprite.Position = new Vector2(px, py + 16f);
            return;
        }
    }

    private Sprout? AcquireSprout()
    {
        foreach (var sprout in _sprouts)
        {
            if (sprout.Sprite.Visible) continue;
            sprout.Age = 0;
            sprout.Phase = Rand(0f, 6.28f);
            sprout.Flip = Rand(0.9f, 1.1f);
            sprout.Sprite.Visible = true;
            sprout.Sprite.Position = Vector2.Zero;
            _activeSprouts.Add(sprout);
            return sprout;
        }
        return null;
    }

    private void UpdateSprouts(double delta)
    {
        const double grow = 0.9, hold = 3.0, fade = 0.8;
        for (var i = _activeSprouts.Count - 1; i >= 0; i--)
        {
            var sprout = _activeSprouts[i];
            sprout.Age += delta;
            var age = sprout.Age;
            float scale, alpha;
            if (age < grow)
            {
                var t = (float)(age / grow);
                scale = 0.25f + 0.75f * t;
                alpha = t;
            }
            else if (age < grow + hold)
            {
                scale = 1f;
                alpha = 1f;
            }
            else if (age < grow + hold + fade)
            {
                var t = (float)((age - grow - hold) / fade);
                scale = 1f - 0.5f * t;
                alpha = 1f - t;
            }
            else
            {
                sprout.Sprite.Visible = false;
                _activeSprouts.RemoveAt(i);
                continue;
            }
            sprout.Sprite.Scale = new Vector2(scale * sprout.Flip, scale);
            sprout.Sprite.Rotation = 0.10f * MathF.Sin((float)age * 2.4f + sprout.Phase);
            var color = sprout.Sprite.Modulate;
            color.A = alpha;
            sprout.Sprite.Modulate = color;
        }
    }

    private float Rand(float min, float max)
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return min + (max - min) * ((_state & 0xFFFFFF) / (float)0xFFFFFF);
    }
}
