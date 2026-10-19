using System;
using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>
/// Simulasi api di grid tile: bahan bakar, kelembapan, angin, hujan, asap.
/// Deterministik penuh (hash per tick), jadi hasilnya sama di tes headless.
/// Api hanya gameplay sejak M4; nyala dekoratif lama tetap di FireAmbience.
/// </summary>
public sealed class FireSystem
{
    private const double SpreadStep = 0.3;
    private const int MaxBurning = 480;

    private Game _game = null!;
    private float[] _fuel = Array.Empty<float>();
    private float[] _moisture = Array.Empty<float>();
    private readonly Dictionary<int, float> _burning = new();
    private readonly List<int> _scratch = new();
    private double _spreadAccumulator;
    private ulong _tick;
    private double _hurtAccumulator;
    private double _smokeAccumulator;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int TileSize { get; private set; } = 16;
    public int BurningCount => _burning.Count;
    public int TotalIgnitions { get; private set; }
    public int TotalExtinguished { get; private set; }
    public int TotalCells => Width * Height;

    public void Configure(Game game)
    {
        _game = game;
        var data = game.World!.Data;
        Width = data.Width;
        Height = data.Height;
        TileSize = data.TileSize;
        _fuel = new float[TotalCells];
        _moisture = new float[TotalCells];
        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                var terrain = TerrainName(data, col, row);
                var index = CellIndex(col, row);
                _fuel[index] = FuelFor(terrain);
                _moisture[index] = BaseMoisture(terrain);
            }
        }
        _burning.Clear();
        _spreadAccumulator = 0;
        _tick = 0;
        TotalIgnitions = 0;
        TotalExtinguished = 0;
    }

    public static float FuelFor(string terrain) => terrain switch
    {
        "water" or "stone" or "rock" or "cobble" or "cave" or "bridge" => 0f,
        "grass" or "meadow" => 1f,
        "dirt" => 0.4f,
        "sand" => 0.15f,
        _ => 0.2f,
    };

    private static float BaseMoisture(string terrain) => terrain switch
    {
        "water" => 1f,
        "sand" => 0.08f,
        "grass" or "meadow" => 0.22f,
        _ => 0.18f,
    };

    private static string TerrainName(WorldData data, int col, int row)
    {
        if (col < 0 || row < 0 || col >= data.Width || row >= data.Height) return "";
        if (data.Terrain.Length <= row || col >= data.Terrain[row].Length) return "";
        var index = data.Terrain[row][col];
        return index >= 0 && index < data.TerrainNames.Count ? data.TerrainNames[index] : "";
    }

    public int CellIndex(int col, int row) => row * Width + col;

    public Vector2 CellCenter(int index) =>
        new((index % Width + 0.5f) * TileSize, (index / Width + 0.5f) * TileSize);

    public bool IgniteCell(int col, int row, float intensity = 0.85f)
    {
        if (col < 0 || row < 0 || col >= Width || row >= Height) return false;
        var index = CellIndex(col, row);
        if (_fuel[index] <= 0.01f || _burning.Count >= MaxBurning) return false;
        if (_burning.TryGetValue(index, out var current))
        {
            _burning[index] = Math.Min(1.2f, Math.Max(current, intensity));
            return true;
        }
        _burning[index] = Math.Clamp(intensity, 0.15f, 1.2f);
        TotalIgnitions++;
        return true;
    }

    public bool IgniteAt(Vector2 pixelPosition) =>
        IgniteCell((int)(pixelPosition.X / TileSize), (int)(pixelPosition.Y / TileSize));

    public float IntensityAt(Vector2 pixelPosition)
    {
        var col = (int)(pixelPosition.X / TileSize);
        var row = (int)(pixelPosition.Y / TileSize);
        if (col < 0 || row < 0 || col >= Width || row >= Height) return 0f;
        return _burning.TryGetValue(CellIndex(col, row), out var value) ? value : 0f;
    }

    public bool IsBurningAt(Vector2 pixelPosition) => IntensityAt(pixelPosition) > 0.05f;

    public float FuelAt(int col, int row) =>
        col < 0 || row < 0 || col >= Width || row >= Height ? 0f : _fuel[CellIndex(col, row)];

    /// <summary>Pasang sekat api (M5): tile jadi tidak bisa terbakar.</summary>
    public bool AddFirebreak(int col, int row)
    {
        if (col < 0 || row < 0 || col >= Width || row >= Height) return false;
        var index = CellIndex(col, row);
        _burning.Remove(index);
        _fuel[index] = 0;
        _moisture[index] = 1;
        return true;
    }

    public bool AddFirebreakAt(Vector2 pixelPosition) =>
        AddFirebreak((int)(pixelPosition.X / TileSize), (int)(pixelPosition.Y / TileSize));

    /// <summary>Padamkan api dalam radius (piksel); balikkan jumlah sel yang padam.</summary>
    public int Extinguish(Vector2 center, float radius)
    {
        var removed = 0;
        var radiusSq = radius * radius;
        foreach (var pair in _burning)
        {
            if (CellCenter(pair.Key).DistanceSquaredTo(center) > radiusSq) continue;
            _burning.Remove(pair.Key);
            _moisture[pair.Key] = Math.Min(1f, _moisture[pair.Key] + 0.7f);
            removed++;
        }
        TotalExtinguished += removed;
        return removed;
    }

    public float SmokeAt(Vector2 pixelPosition)
    {
        if (_burning.Count == 0) return 0f;
        var wind = _game.Weather!.WindVector;
        var windDir = wind.LengthSquared() > 0.0001f ? wind.Normalized() : Vector2.Zero;
        var radius = 6f * TileSize;
        var sum = 0f;
        foreach (var pair in _burning)
        {
            var toPoint = pixelPosition - CellCenter(pair.Key);
            var distance = toPoint.Length();
            if (distance > radius) continue;
            var falloff = 1f - distance / radius;
            var drift = 0.7f;
            if (windDir != Vector2.Zero && distance > 1f)
                drift += 0.55f * Math.Max(0f, toPoint.Normalized().Dot(windDir)) * _game.Weather.WindStrength;
            sum += pair.Value * falloff * drift;
        }
        return Math.Clamp(sum / 5.5f, 0f, 1f);
    }

    /// <summary>Efek api/asap ke pemain (damage api + sesak napas saat napas habis).</summary>
    public void TickPlayer(double delta)
    {
        var player = _game.Player;
        if (player is null || !player.Alive) return;

        var intensity = IntensityAt(player.Position);
        if (intensity > 0.05f)
        {
            _hurtAccumulator += delta;
            if (_hurtAccumulator >= 0.6)
            {
                _hurtAccumulator = 0;
                var damage = 4 + (int)(intensity * 6);
                _game.DamageFromHazard(damage, "terbakar");
            }
        }
        else
        {
            _hurtAccumulator = 0;
        }

        if (player.SmokeExposure > 0.35f && player.Energy <= 0.01)
        {
            _smokeAccumulator += delta;
            if (_smokeAccumulator >= 1.0)
            {
                _smokeAccumulator = 0;
                _game.DamageFromHazard(Rules.SmokeHurt, "sesak asap");
            }
        }
        else
        {
            _smokeAccumulator = 0;
        }
    }

    public void Advance(double delta)
    {
        if (_burning.Count == 0) return;
        var weather = _game.Weather!;
        var rain = weather.RainAmount;

        _scratch.Clear();
        _scratch.AddRange(_burning.Keys);
        foreach (var index in _scratch)
        {
            if (!_burning.TryGetValue(index, out var intensity)) continue;
            var moisture = Math.Min(1f, _moisture[index] + weather.MoistureBoost);
            var decay = 0.012f + moisture * 0.09f + rain * 0.42f;
            intensity -= (float)(decay * delta);
            _fuel[index] = Math.Max(0f, _fuel[index] - (float)(0.032 * delta * (0.4 + intensity)));
            if (_fuel[index] <= 0.001f) intensity -= (float)(0.5 * delta);
            if (rain > 0.05f) intensity -= (float)(rain * 0.11 * delta);
            if (intensity <= 0.03f) _burning.Remove(index);
            else _burning[index] = intensity;
        }

        _spreadAccumulator += delta;
        while (_spreadAccumulator >= SpreadStep)
        {
            _spreadAccumulator -= SpreadStep;
            _tick++;
            SpreadOnce(weather);
        }
    }

    private void SpreadOnce(WeatherSystem weather)
    {
        if (_burning.Count == 0 || _burning.Count >= MaxBurning) return;
        var wind = weather.WindVector;
        var windDir = wind.LengthSquared() > 0.0001f ? wind.Normalized() : Vector2.Zero;
        var rainFactor = 1f - 0.92f * weather.RainAmount;

        _scratch.Clear();
        _scratch.AddRange(_burning.Keys);
        foreach (var index in _scratch)
        {
            if (!_burning.TryGetValue(index, out var intensity) || intensity < 0.25f) continue;
            var col = index % Width;
            var row = index / Width;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nc = col + dx;
                    var nr = row + dy;
                    if (nc < 0 || nr < 0 || nc >= Width || nr >= Height) continue;
                    var nIndex = CellIndex(nc, nr);
                    if (_fuel[nIndex] <= 0.01f || _burning.ContainsKey(nIndex)) continue;
                    var moisture = Math.Min(1f, _moisture[nIndex] + weather.MoistureBoost);
                    var align = windDir == Vector2.Zero
                        ? 0f
                        : Math.Max(0f, new Vector2(dx, dy).Normalized().Dot(windDir));
                    var windFactor = 0.55f + 1.9f * weather.WindStrength * align;
                    var chance = 0.055f * intensity * _fuel[nIndex] * (1f - moisture) * windFactor * rainFactor;
                    if (Hash01(nc, nr, _tick) >= chance) continue;
                    _burning[nIndex] = 0.4f;
                    TotalIgnitions++;
                }
            }
        }
    }

    public void Clear()
    {
        _burning.Clear();
        _spreadAccumulator = 0;
        _hurtAccumulator = 0;
        _smokeAccumulator = 0;
    }

    /// <summary>Jumlah sel api di dalam radius (dipakai insiden M6).</summary>
    public int CountBurningNear(Vector2 center, float radius)
    {
        var count = 0;
        var radiusSq = radius * radius;
        foreach (var pair in _burning)
            if (CellCenter(pair.Key).DistanceSquaredTo(center) <= radiusSq) count++;
        return count;
    }

    /// <summary>Sel api untuk visual: pusat sel + intensitas.</summary>
    public readonly record struct FireCell(Vector2 Position, float Intensity);

    public IEnumerable<FireCell> Cells()
    {
        foreach (var pair in _burning)
            yield return new FireCell(CellCenter(pair.Key), pair.Value);
    }

    private static float Hash01(int col, int row, ulong tick)
    {
        var value = (ulong)(col * 73856093) ^ (ulong)(row * 19349663) ^ (tick * 0x9E3779B97F4A7C15UL);
        value ^= value >> 33;
        value *= 0xFF51AFD7ED558CCDUL;
        value ^= value >> 33;
        value *= 0xC4CEB9FE1A85EC53UL;
        value ^= value >> 33;
        return (value & 0xFFFFFFUL) / 16777216f;
    }
}
