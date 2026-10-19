using System;
using Godot;

namespace RpgGame;

public enum WeatherKind
{
    Dry,
    Windy,
    Rain,
}

/// <summary>
/// Cuaca & angin deterministik: kemarau, berangin, hujan deras. Angin
/// menggerakkan api dan asap; hujan menaikkan kelembapan dan memadamkan api.
/// Tidak bergantung pada frame — aman diuji headless.
/// </summary>
public sealed class WeatherSystem
{
    public const double StageSeconds = 80;
    public const int StageCount = 3;

    public WeatherKind Kind { get; private set; } = WeatherKind.Dry;
    public long Stage { get; private set; }
    public float WindAngle { get; private set; }
    public float WindStrength { get; private set; }
    public float RainAmount { get; private set; }

    /// <summary>
    /// F1: dorongan hujan 0..1 dari kesehatan hutan (DaySystem). Makin tinggi,
    /// makin besar peluang stage berikutnya berisi hujan.
    /// </summary>
    public float RainBias { get; set; }

    private double _elapsed;

    public Vector2 WindVector => new(MathF.Cos(WindAngle) * WindStrength, MathF.Sin(WindAngle) * WindStrength);

    public string Name => Kind switch
    {
        WeatherKind.Windy => "Berangin",
        WeatherKind.Rain => "Hujan",
        _ => "Kemarau",
    };

    public string Label
    {
        get
        {
            var arrow = ArrowFor(WindAngle);
            var wind = Kind == WeatherKind.Dry ? "angin lemah" : $"angin {arrow}";
            return Kind == WeatherKind.Rain ? $"{Name} · {wind} · basah" : $"{Name} · {wind}";
        }
    }

    /// <summary>Kelembapan tambahan dari cuaca (0..1) yang dipakai simulasi api.</summary>
    public float MoistureBoost => RainAmount * 0.65f;

    public void Advance(double delta)
    {
        _elapsed += delta;
        if (_elapsed < StageSeconds) return;
        _elapsed -= StageSeconds;
        SetStage(Stage + 1);
    }

    /// <summary>Paksa cuaca (dipakai debug, CLI, dan tes).</summary>
    public void Force(WeatherKind kind, float windAngle, float windStrength)
    {
        Stage = 0;
        _elapsed = 0;
        Apply(kind, windAngle, windStrength);
    }

    public void SetStage(long stage)
    {
        Stage = Math.Max(0, stage);
        var roll = Hash01((ulong)Stage * 0x9E3779B1UL + 0xA511E9B3UL);
        var rainCut = Math.Clamp(RainBias, 0f, 1f) * 0.35f;
        var kind = roll < 0.5f - rainCut ? WeatherKind.Dry
            : roll < 0.8f - rainCut ? WeatherKind.Windy
            : WeatherKind.Rain;
        var angle = Hash01((ulong)Stage * 0xC2B2AE35UL + 0x27D4EB2FUL) * MathF.Tau;
        var strength = kind switch
        {
            WeatherKind.Windy => 0.62f + Hash01((ulong)Stage * 0x165667B1UL + 7UL) * 0.30f,
            WeatherKind.Rain => 0.30f + Hash01((ulong)Stage * 0x165667B1UL + 7UL) * 0.25f,
            _ => 0.10f + Hash01((ulong)Stage * 0x165667B1UL + 7UL) * 0.16f,
        };
        Apply(kind, angle, strength);
    }

    private void Apply(WeatherKind kind, float angle, float strength)
    {
        Kind = kind;
        WindAngle = ((angle % MathF.Tau) + MathF.Tau) % MathF.Tau;
        WindStrength = Math.Clamp(strength, 0f, 1f);
        RainAmount = kind == WeatherKind.Rain ? 0.72f + Hash01((ulong)Stage * 0x85EBCA6BUL + 11UL) * 0.28f : 0f;
    }

    public static string ArrowFor(float angle)
    {
        var normalized = ((angle % MathF.Tau) + MathF.Tau) % MathF.Tau;
        var index = (int)MathF.Round(normalized / (MathF.Tau / 8f)) % 8;
        return index switch
        {
            0 => "→",
            1 => "↘",
            2 => "↓",
            3 => "↙",
            4 => "←",
            5 => "↖",
            6 => "↑",
            _ => "↗",
        };
    }

    /// <summary>Hash deterministik 0..1 (dipakai cuaca; jangan diubah nilainya).</summary>
    public static float Hash01(ulong value)
    {
        value ^= value >> 33;
        value *= 0xFF51AFD7ED558CCDUL;
        value ^= value >> 33;
        value *= 0xC4CEB9FE1A85EC53UL;
        value ^= value >> 33;
        return (value & 0xFFFFFFUL) / 16777216f;
    }
}
