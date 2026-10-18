using System;
using System.Text.Json;
using Godot;

namespace RpgGame;

/// <summary>
/// Pemuatan aset. PNG dibaca sebagai byte lalu dijadikan ImageTexture supaya
/// piksel tetap persis (tanpa filter/kompresi importer Godot) — penting untuk
/// pixel art yang dihasilkan tools/generate_*.py.
/// </summary>
public static class Assets
{
    public const string TilesetPng = "res://assets/tiles/tileset.png";
    public const string TilesetJson = "res://assets/tiles/tileset.json";
    public const string WorldJson = "res://assets/world/overworld.json";
    public const string HeroPng = "res://assets/character/hero_atlas.png";
    public const string HeroJson = "res://assets/character/hero.json";
    public const string EnemyPng = "res://assets/character/enemy_atlas.png";
    public const string EnemyJson = "res://assets/character/enemy.json";
    public const string NpcPng = "res://assets/character/npc_atlas.png";
    public const string NpcJson = "res://assets/character/npc.json";
    public const string PixelFont = "res://assets/font/pixel.fnt";
    public const string FxNoisePng = "res://assets/fx/fx_noise.png";
    public const string FxLightSoftPng = "res://assets/fx/fx_light_soft.png";
    public const string FxGlowSoftPng = "res://assets/fx/fx_glow_soft.png";
    public const string FxFlamePng = "res://assets/fx/fx_flame.png";
    public const string FxSproutPng = "res://assets/fx/fx_sprout.png";
    public const string FxVignettePng = "res://assets/fx/fx_vignette.png";
    public const string FxSmokeShader = "res://assets/shaders/fx_smoke.gdshader";
    public const string FxGlowShader = "res://assets/shaders/fx_glow.gdshader";
    public const string FxGradeShader = "res://assets/shaders/fx_grade.gdshader";
    public const string FxHeatShader = "res://assets/shaders/fx_heat.gdshader";

    public static Texture2D LoadTexture(string resPath)
    {
        var bytes = FileAccess.GetFileAsBytes(resPath);
        if (bytes is null || bytes.Length == 0)
            throw new InvalidOperationException($"aset tidak ditemukan / kosong: {resPath}");
        var image = new Image();
        var err = image.LoadPngFromBuffer(bytes);
        if (err != Error.Ok)
            throw new InvalidOperationException($"gagal decode PNG {resPath}: {err}");
        image.Convert(Image.Format.Rgba8);
        return ImageTexture.CreateFromImage(image);
    }

    public static JsonDocument LoadJson(string resPath)
    {
        var text = FileAccess.GetFileAsString(resPath);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"json tidak ditemukan / kosong: {resPath}");
        return JsonDocument.Parse(text);
    }

    /// <summary>
    /// Shader dibaca sebagai teks lalu dibuat Shader resource di kode (sama seperti
    /// PNG) supaya tidak bergantung pada importir Godot dan tetap 1 sumber kebenaran.
    /// </summary>
    public static Shader LoadShader(string resPath)
    {
        var text = FileAccess.GetFileAsString(resPath);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"shader tidak ditemukan / kosong: {resPath}");
        return new Shader { Code = text };
    }

    /// <summary>Piksel putih 1x1 untuk quad heat haze (warnanya diabaikan shader).</summary>
    public static Texture2D MakeSolidPixel()
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, new Color(1, 1, 1, 1));
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Tekstur bayangan lembut di bawah karakter (dipakai semua aktor).</summary>
    public static Texture2D MakeShadow(int w = 14, int h = 6)
    {
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        var cx = (w - 1) / 2.0;
        var cy = (h - 1) / 2.0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var dx = (x - cx) / (w / 2.0);
                var dy = (y - cy) / (h / 2.0);
                var d = dx * dx + dy * dy;
                if (d <= 1.0)
                    image.SetPixel(x, y, new Color(0, 0, 0, d < 0.45f ? 0.42f : 0.24f));
            }
        }
        return ImageTexture.CreateFromImage(image);
    }
}
