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
