using Godot;

namespace RpgGame;

/// <summary>
/// Pipeline FX layar penuh untuk tema "Arga: Api & Asap":
///   1. asap/kabut berlapis  (fx_smoke)  — abu-arang, mengikuti kamera
///   2. bloom ringan          (fx_glow)   — bright-pass 9 tap
///   3. color grading+vignette (fx_grade) — pass terakhir sebelum HUD
/// plus heat haze lokal (fx_heat) yang ditempel sebagai quad di titik api.
///
/// Semua murni visual: tidak menyentuh Rules, damage, kolisi, atau AI.
/// CanvasLayer layer 5 — di atas dunia (0) dan di bawah HUD (10), jadi HUD
/// tetap tajam tanpa vignette/grading.
/// </summary>
public sealed partial class Fx : CanvasLayer
{
    public ShaderMaterial SmokeMaterial { get; }
    public ShaderMaterial GlowMaterial { get; }
    public ShaderMaterial GradeMaterial { get; }
    public ShaderMaterial HeatMaterial { get; }

    /// <summary>jumlah quad heat haze yang terpasang (harus = World.HeatSources.Count).</summary>
    public int HeatSourceCount { get; private set; }

    /// <summary>Ketebalan asap 0..0.75; naik di zona terbakar (tahap berikutnya).</summary>
    public float AshDensity { get; set; } = 0.20f;

    /// <summary>0 = siang, 1 = malam pekat (diisi oleh siklus siang/malam tahap lighting).</summary>
    public float NightAmount { get; set; }

    private readonly Vector2 _viewport = new(480, 270);
    private Vector2 _camCenter;

    public Fx()
    {
        Name = "Fx";
        Layer = 5;

        var noise = Assets.LoadTexture(Assets.FxNoisePng);

        SmokeMaterial = new ShaderMaterial { Shader = Assets.LoadShader(Assets.FxSmokeShader) };
        SmokeMaterial.SetShaderParameter("noise_tex", noise);
        SmokeMaterial.SetShaderParameter("viewport_size", _viewport);
        SmokeMaterial.SetShaderParameter("cam_px", Vector2.Zero);
        SmokeMaterial.SetShaderParameter("ash", AshDensity);

        GlowMaterial = new ShaderMaterial { Shader = Assets.LoadShader(Assets.FxGlowShader) };
        GlowMaterial.SetShaderParameter("viewport_size", _viewport);

        GradeMaterial = new ShaderMaterial { Shader = Assets.LoadShader(Assets.FxGradeShader) };
        GradeMaterial.SetShaderParameter("viewport_size", _viewport);
        GradeMaterial.SetShaderParameter("night", 0f);

        HeatMaterial = new ShaderMaterial { Shader = Assets.LoadShader(Assets.FxHeatShader) };
        HeatMaterial.SetShaderParameter("noise_tex", noise);
        HeatMaterial.SetShaderParameter("viewport_size", _viewport);

        AddFullscreenLayer("Smoke", SmokeMaterial);
        AddFullscreenLayer("Glow", GlowMaterial);
        AddFullscreenLayer("Grade", GradeMaterial);
    }

    private void AddFullscreenLayer(string name, ShaderMaterial material)
    {
        var rect = new ColorRect
        {
            Name = name,
            Color = new Color(1, 1, 1, 1),
            Material = material,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(rect);
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rect.OffsetRight = rect.OffsetBottom = 0;
    }

    /// <summary>Titik tengah kamera (piksel dunia) — asap ikut dunia, bukan layar.</summary>
    public void SetCameraCenter(Vector2 center) => _camCenter = center;

    /// <summary>
    /// Pasang quad heat haze di posisi api. `parent` harus node dunia yang tidak
    /// ikut y-sort supaya shimmer selalu di atas sprite lain di dunia.
    /// </summary>
    public void AttachHeat(Node2D parent, Vector2 worldPosition, float size, float phase)
    {
        var material = (ShaderMaterial)HeatMaterial.Duplicate();
        material.SetShaderParameter("seed", phase);
        parent.AddChild(new Sprite2D
        {
            Name = $"Heat_{HeatSourceCount}",
            Texture = Assets.MakeSolidPixel(),
            Centered = true,
            Position = worldPosition,
            Scale = new Vector2(size, size),
            Material = material,
            ZIndex = 10,
        });
        HeatSourceCount++;
    }

    public override void _Process(double delta)
    {
        SmokeMaterial.SetShaderParameter("cam_px", _camCenter);
        SmokeMaterial.SetShaderParameter("ash", AshDensity);
        GradeMaterial.SetShaderParameter("night", NightAmount);
    }
}
