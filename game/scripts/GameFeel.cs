using System;
using Godot;

namespace RpgGame;

/// <summary>
/// "Game feel" murni visual (tahap 2f) — tidak menyentuh damage, kolisi, atau AI:
///   · camera shake singkat saat pukulan kena / pemain terluka
///   · hit-stop (freeze frame) agar pukulan terasa berbobot
///   · debu langkah & debu mendarat dari pool partikel
///   · percikan saat musuh kena dan saat peti dibuka
/// Semua state deterministik supaya bisa diuji headless.
/// </summary>
public sealed partial class GameFeel : Node2D
{
    public const int DustCapacity = 48;
    public const int SparkCapacity = 32;
    public const float ShakeDecayPerSecond = 9f;
    public const float MaxCameraOffset = 4f;

    public ParticlePool? Dust { get; private set; }
    public ParticlePool? Sparks { get; private set; }

    public float ShakeAmount { get; private set; }
    public double HitStopRemaining { get; private set; }
    public bool Frozen => HitStopRemaining > 0;
    public Vector2 CameraOffset { get; private set; }
    public long DustSpawned { get; private set; }
    public long SparkSpawned { get; private set; }

    private uint _state = 0x9E3779B9u;

    public void Configure()
    {
        Name = "GameFeel";
        YSortEnabled = false;
        Dust = new ParticlePool("Dust", Assets.LoadTexture(Assets.FxGlowSoftPng), DustCapacity);
        Sparks = new ParticlePool("Sparks", Assets.LoadTexture(Assets.FxGlowSoftPng), SparkCapacity);
        AddChild(Dust);
        AddChild(Sparks);
    }

    /// <summary>Guncang kamera (nilai di-pertahankan yang terbesar, tidak menumpuk).</summary>
    public void Shake(float amount) => ShakeAmount = MathF.Max(ShakeAmount, amount);

    /// <summary>Bekukan sebentar dunia (freeze frame): dipanggil saat pukulan kena.</summary>
    public void HitStop(double seconds) => HitStopRemaining = Math.Max(HitStopRemaining, seconds);

    /// <summary>Debu kecil di kaki saat melangkah (lebih banyak kalau berlari).</summary>
    public void StepPuff(Vector2 feet, bool running)
    {
        if (Dust is null) return;
        var count = running ? 2 : 1;
        for (var i = 0; i < count; i++)
        {
            if (Dust.Spawn(feet + new Vector2(Rand(-3f, 3f), -1f),
                    new Vector2(Rand(-7f, 7f), Rand(-7f, -2f)),
                    Rand(0.26f, 0.44f), new Color(0.74f, 0.70f, 0.62f, 0.5f),
                    Rand(0.04f, 0.07f), 0.018f, -5f, 0.5f, Rand(-1f, 1f),
                    Rand(2f, 5f), Rand(3f, 6f), Rand(0f, 6.28f)))
                DustSpawned++;
        }
    }

    /// <summary>Debu mendarat: lingkaran kecil yang menyebar saat kaki menyentuh tanah.</summary>
    public void LandingPuff(Vector2 feet)
    {
        if (Dust is null) return;
        for (var i = 0; i < 3; i++)
        {
            var side = i == 1 ? 1f : -1f;
            if (Dust.Spawn(feet + new Vector2(side * Rand(1f, 4f), -1f),
                    new Vector2(side * Rand(8f, 16f), Rand(-8f, -3f)),
                    Rand(0.3f, 0.5f), new Color(0.74f, 0.70f, 0.62f, 0.55f),
                    Rand(0.05f, 0.08f), 0.02f, -6f, 0.4f, 0f,
                    Rand(2f, 4f), Rand(3f, 6f), Rand(0f, 6.28f)))
                DustSpawned++;
        }
    }

    /// <summary>Percikan radial (musuh kena / peti dibuka).</summary>
    public void Burst(Vector2 at, Color color, int count)
    {
        if (Sparks is null) return;
        for (var i = 0; i < count; i++)
        {
            var angle = Rand(0f, 6.283f);
            var speed = Rand(24f, 62f);
            if (Sparks.Spawn(at + new Vector2(Rand(-2f, 2f), Rand(-2f, 2f)),
                    new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed - 12f),
                    Rand(0.22f, 0.4f), color,
                    Rand(0.04f, 0.065f), 0.01f, 42f, 0.6f, 0f,
                    0f, 0f, 0f))
                SparkSpawned++;
        }
    }

    /// <summary>Majukan shake & umur partikel (dipanggil tiap frame).</summary>
    public void Advance(double delta)
    {
        var dt = (float)delta;
        ShakeAmount = MathF.Max(0f, ShakeAmount - dt * ShakeDecayPerSecond);
        CameraOffset = ShakeAmount > 0.01f
            ? (new Vector2(Rand(-1f, 1f), Rand(-1f, 1f)) * ShakeAmount).LimitLength(MaxCameraOffset)
            : Vector2.Zero;
        HitStopRemaining = Math.Max(0, HitStopRemaining - delta);

        var wind = new Vector2(6f, 0f);
        Dust?.Advance(delta, wind);
        Sparks?.Advance(delta, wind);
    }

    private float Rand(float min, float max)
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return min + (max - min) * ((_state & 0xFFFFFF) / (float)0xFFFFFF);
    }
}
