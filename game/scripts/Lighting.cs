using System;
using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>
/// Siklus siang/malam + penerangan titik api — murni visual, tidak menyentuh
/// Rules/kolisi/AI:
///   · CanvasModulate  -> warna ambient dunia berubah mengikuti jam (0..1)
///   · PointLight2D    -> cahaya hangat berkedip di tiap titik api (fx_light_soft)
/// Node ini ditaruh di canvas dunia, jadi HUD (CanvasLayer 10) tidak ikut digelapkan.
/// </summary>
public sealed partial class Lighting : Node2D
{
    private const float Tau = MathF.PI * 2f;

    /// <summary>0 = tengah malam, 0.5 = tengah hari; berputar tiap `DayLength` detik.</summary>
    public float TimeOfDay { get; private set; } = 0.5f;

    /// <summary>Durasi satu hari penuh (detik bermain).</summary>
    public float DayLength { get; set; } = 300f;

    /// <summary>0 = siang terang, 1 = malam pekat (diteruskan ke grading Fx).</summary>
    public float NightAmount { get; private set; }

    public CanvasModulate Modulate { get; }

    /// <summary>Cahaya di setiap titik api (urutan sama dengan World.HeatSources).</summary>
    public readonly List<PointLight2D> Lights = new();

    public static readonly Color DayColor = new(1f, 1f, 1f);
    public static readonly Color NightColor = new(0.42f, 0.47f, 0.68f);
    private static readonly Color FlameColor = new(1f, 0.78f, 0.5f);

    private readonly List<float> _phases = new();
    private Fx? _fx;

    public Lighting()
    {
        Name = "Lighting";
        YSortEnabled = false;
        Modulate = new CanvasModulate { Name = "DayNight" };
        AddChild(Modulate);
    }

    /// <summary>
    /// Pasang satu PointLight2D per titik api. `fx` (opsional) menerima `NightAmount`
    /// supaya grading malam ikut berubah.
    /// </summary>
    public void Configure(IReadOnlyList<(Vector2 Pos, float Phase, float Size)> sources,
        Texture2D lightTexture, Fx? fx = null)
    {
        _fx = fx;
        foreach (var (pos, phase, size) in sources)
        {
            var scale = size * 1.5f / 64f;   // tekstur 128 px -> radius = 64 * scale
            var light = new PointLight2D
            {
                Name = $"Flame_{Lights.Count}",
                Texture = lightTexture,
                Color = FlameColor,
                BlendMode = Light2D.BlendModeEnum.Add,
                Position = pos,
                TextureScale = scale,
                Energy = 1f,
            };
            AddChild(light);
            Lights.Add(light);
            _phases.Add(phase);
        }
        Apply(0f);
    }

    public void SetTimeOfDay(float timeOfDay)
    {
        TimeOfDay = Wrap(timeOfDay);
        Apply(0f);
    }

    /// <summary>Majukan jam dunia; visual (warna ambient + nyala api) ikut diperbarui.</summary>
    public void Advance(double delta)
    {
        if (DayLength > 0.01f)
            TimeOfDay = Wrap(TimeOfDay + (float)delta / DayLength);
        Apply(Time.GetTicksMsec() / 1000f);
    }

    private static float Wrap(float t)
    {
        t %= 1f;
        return t < 0f ? t + 1f : t;
    }

    private void Apply(float seconds)
    {
        // malam mengikuti kurva cos: pendek di tengah malam, panjang di siang
        NightAmount = MathF.Pow(0.5f + 0.5f * MathF.Cos(Tau * TimeOfDay), 1.7f);
        Modulate.Color = DayColor.Lerp(NightColor, NightAmount);
        if (_fx is not null) _fx.NightAmount = NightAmount;

        // kedipan sama dengan prop obor/lentera di Game (sinkron fase);
        // energi = 0.85 * (0.3 siang .. 1.0 malam) supaya tidak over-bright
        for (var i = 0; i < Lights.Count; i++)
        {
            var phase = _phases[i];
            var flicker = 0.82f + 0.18f * MathF.Sin(seconds * 6.5f + phase) *
                MathF.Sin(seconds * 2.7f + phase * 1.7f);
            Lights[i].Energy = 0.85f * (0.3f + 0.7f * NightAmount) * flicker;
        }
    }
}
