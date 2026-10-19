using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RpgGame;

/// <summary>
/// F1 — musim kemarau berhari-hari. Hari berganti tiap <c>day_seconds</c>, api
/// liar menyulut situs terjadwal (deterministik), dua meter (keselamatan desa &
/// kesehatan hutan) bergerak mengikuti aktivitas api, dan warga desa mengungsi
/// saat desa terancam. Semua angka ada di <c>assets/data/fire_season.json</c>.
/// </summary>
public sealed class DaySystem
{
    public const string Path = "res://assets/data/fire_season.json";
    public const string EvacFlagPrefix = "ungsi_";

    /// <summary>Semua warga; urutan tetap supaya tes deterministik.</summary>
    public static readonly string[] Villagers = { "mira", "bram", "sari", "lena", "tomas" };

    /// <summary>Warga yang lebih dulu mengungsi (bukan tokoh inti misi).</summary>
    private static readonly string[] NonEssential = { "bram", "sari", "tomas" };

    private readonly Game _game;
    private readonly Dictionary<int, List<Vector2I>> _schedule = new();
    private readonly List<Vector2I> _pool = new();

    public int Day { get; private set; } = 1;
    public int DayCount { get; private set; } = 14;
    public double DaySeconds { get; private set; } = 300;
    public float VillageSafety { get; private set; } = 100f;
    public float ForestHealth { get; private set; } = 100f;
    public float MaskSmokeFactor { get; private set; } = 0.35f;
    public float RainEnergyBonus { get; private set; } = 0.5f;
    public float MetersFloor { get; private set; } = 0.65f;

    /// <summary>Sel api yang menyala dalam radius ancaman desa (frame terakhir).</summary>
    public int ThreatCells { get; private set; }

    /// <summary>Semua sel api yang menyala (frame terakhir).</summary>
    public int BurningCells { get; private set; }

    public bool VillageThreatened => ThreatCells > 0;

    private int _firstDay = 2;
    private int _maxPerDay = 2;
    private float _intensity = 0.8f;
    private Rect2I _villageRect = new(48, 36, 16, 12);
    private int _threatTiles = 8;
    private float _villageDrain = 2.4f;
    private float _villageRecover = 0.8f;
    private float _forestDrain = 0.5f;
    private float _forestRecover = 0.6f;
    private float _sproutBonus = 3f;
    private int _evacuateBelow = 60;
    private int _fullEvacuateBelow = 30;
    private int _returnAbove = 75;
    private int _rainThreshold = 70;
    private float _rainBias = 0.35f;
    private double _elapsed;
    private int _lastRestored;
    private int _debugIgnitions;

    public DaySystem(Game game) => _game = game;

    public void Configure()
    {
        _schedule.Clear();
        _pool.Clear();
        using var doc = Assets.LoadJson(Path);
        var root = doc.RootElement;
        DayCount = root.GetProperty("day_count").GetInt32();
        DaySeconds = root.GetProperty("day_seconds").GetDouble();

        var wildfire = root.GetProperty("wildfire");
        _firstDay = wildfire.GetProperty("first_day").GetInt32();
        _maxPerDay = wildfire.GetProperty("max_per_day").GetInt32();
        _intensity = wildfire.GetProperty("intensity").GetSingle();
        foreach (var pair in wildfire.GetProperty("schedule").EnumerateObject())
            _schedule[int.Parse(pair.Name)] = pair.Value.EnumerateArray().Select(ParseSite).ToList();
        foreach (var site in wildfire.GetProperty("pool").EnumerateArray()) _pool.Add(ParseSite(site));

        var village = root.GetProperty("village");
        var rect = village.GetProperty("rect").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        _villageRect = new Rect2I(rect[0], rect[1], rect[2], rect[3]);
        _threatTiles = village.GetProperty("threat_tiles").GetInt32();
        _villageDrain = village.GetProperty("drain_per_cell_sec").GetSingle();
        _villageRecover = village.GetProperty("recover_per_sec").GetSingle();
        _evacuateBelow = village.GetProperty("evacuate_below").GetInt32();
        _fullEvacuateBelow = village.GetProperty("full_evacuate_below").GetInt32();
        _returnAbove = village.GetProperty("return_above").GetInt32();

        var forest = root.GetProperty("forest");
        _forestDrain = forest.GetProperty("drain_per_cell_sec").GetSingle();
        _forestRecover = forest.GetProperty("recover_per_sec").GetSingle();
        _sproutBonus = forest.GetProperty("sprout_bonus").GetSingle();

        var rain = root.GetProperty("rain");
        _rainThreshold = rain.GetProperty("health_threshold").GetInt32();
        _rainBias = rain.GetProperty("bias").GetSingle();
        RainEnergyBonus = rain.GetProperty("energy_bonus").GetSingle();

        MaskSmokeFactor = root.GetProperty("mask").GetProperty("smoke_factor").GetSingle();
        MetersFloor = root.GetProperty("ending").GetProperty("meters_floor").GetSingle();

        Day = 1;
        _elapsed = 0;
        VillageSafety = 100f;
        ForestHealth = 100f;
        _lastRestored = _game.Player?.State.Restored ?? 0;
    }

    private static Vector2I ParseSite(JsonElement site)
    {
        var values = site.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        return new Vector2I(values[0], values[1]);
    }

    /// <summary>Detik yang sudah berjalan di hari ini (0..day_seconds).</summary>
    public double SecondsIntoDay => _elapsed;

    /// <summary>Pulihkan dari save: hari + meter; ungsian dibaca ulang dari flag.</summary>
    public void Restore(int day, float village, float forest)
    {
        Day = Math.Clamp(day, 1, DayCount);
        VillageSafety = Math.Clamp(village, 0f, 100f);
        ForestHealth = Math.Clamp(forest, 0f, 100f);
        _elapsed = 0;
        _lastRestored = _game.Player?.State.Restored ?? 0;
        CountFires();
        SyncVisibility();
    }

    public void Advance(double delta)
    {
        if (_game.Player is null || _game.Fire is null) return;
        _elapsed += delta;
        while (_elapsed >= DaySeconds && Day < DayCount)
        {
            _elapsed -= DaySeconds;
            AdvanceDay();
        }
        if (Day >= DayCount && _elapsed > DaySeconds) _elapsed = DaySeconds;
        CountFires();
        UpdateMeters(delta);
        _game.Weather.RainBias = ForestHealth >= _rainThreshold ? _rainBias : 0f;
        SyncVisibility();
    }

    /// <summary>Ganti hari: sulut api liar terjadwal. Dipakai juga tombol debug F1.</summary>
    public void AdvanceDay()
    {
        if (Day >= DayCount) return;
        Day++;
        var ignited = 0;
        foreach (var site in SitesForDay(Day))
        {
            if (ignited >= _maxPerDay) break;
            if (_game.Fire.IgniteCell(site.X, site.Y, _intensity)) ignited++;
        }
        _game.Lighting?.SetTimeOfDay(0.28f);
        _game.Bus.RaiseDayAdvanced(Day);
        _game.Hud?.Toast($"Hari {Day}/{DayCount} — kemarau memanas, api liar menyala.");
        _game.LogEvent("hari", $"{Day}/{DayCount}");
        _game.Autosave("hari baru");
    }

    /// <summary>Situs api liar untuk satu hari; hari tanpa jadwal memakai pool deterministik.</summary>
    public List<Vector2I> SitesForDay(int day)
    {
        if (_schedule.TryGetValue(day, out var scheduled)) return scheduled;
        if (day < _firstDay || _pool.Count == 0) return new List<Vector2I>();
        var pick = _pool[(int)(WeatherSystem.Hash01((ulong)day * 31UL + 7UL) * _pool.Count) % _pool.Count];
        return new List<Vector2I> { pick };
    }

    private void CountFires()
    {
        ThreatCells = 0;
        BurningCells = 0;
        foreach (var cell in _game.Fire.Cells())
        {
            BurningCells++;
            var col = (int)(cell.Position.X / _game.Fire.TileSize);
            var row = (int)(cell.Position.Y / _game.Fire.TileSize);
            if (NearVillage(col, row)) ThreatCells++;
        }
    }

    private bool NearVillage(int col, int row)
    {
        var left = _villageRect.Position.X;
        var top = _villageRect.Position.Y;
        var right = left + _villageRect.Size.X - 1;
        var bottom = top + _villageRect.Size.Y - 1;
        var dx = Math.Max(0, Math.Max(left - col, col - right));
        var dy = Math.Max(0, Math.Max(top - row, row - bottom));
        return Math.Max(dx, dy) <= _threatTiles;
    }

    private void UpdateMeters(double delta)
    {
        var step = (float)delta;
        VillageSafety = ThreatCells > 0
            ? Math.Max(0f, VillageSafety - _villageDrain * ThreatCells * step)
            : Math.Min(100f, VillageSafety + _villageRecover * step);
        ForestHealth = BurningCells > 0
            ? Math.Max(0f, ForestHealth - _forestDrain * BurningCells * step)
            : Math.Min(100f, ForestHealth + _forestRecover * step);
        var restored = _game.Player?.State.Restored ?? _lastRestored;
        if (restored > _lastRestored)
        {
            ForestHealth = Math.Min(100f, ForestHealth + _sproutBonus * (restored - _lastRestored));
            _lastRestored = restored;
        }
        EvaluateEvacuation();
    }

    public bool IsEvacuated(string id) =>
        _game.Player?.State.Flags.GetValueOrDefault(EvacFlagPrefix + id) == true;

    /// <summary>
    /// Ambang ungsian dengan histeresis: &lt;30 semua warga, &lt;60 warga non-inti,
    /// &gt;=75 semua kembali. Di antaranya status tidak berubah (biar tidak bolak-balik).
    /// </summary>
    private void EvaluateEvacuation()
    {
        if (_game.Player is null) return;
        List<string>? target = null;
        if (VillageSafety < _fullEvacuateBelow) target = Villagers.ToList();
        else if (VillageSafety < _evacuateBelow) target = NonEssential.ToList();
        else if (VillageSafety >= _returnAbove) target = new List<string>();
        if (target is null) return;

        var moved = new List<string>();
        foreach (var id in Villagers)
        {
            var flag = EvacFlagPrefix + id;
            var should = target.Contains(id);
            var current = _game.Player.State.Flags.GetValueOrDefault(flag);
            if (should == current) continue;
            _game.Player.State.Flags[flag] = should;
            moved.Add(should ? $"{id} mengungsi" : $"{id} kembali");
        }
        if (moved.Count == 0) return;
        CountFires();
        SyncVisibility();
        _game.Hud?.Toast("Warga desa: " + string.Join(", ", moved) + ".");
        _game.LogEvent("ungsian", string.Join(",", moved));
        _game.Autosave("ungsian warga");
    }

    /// <summary>Sembunyikan / tampilkan NPC sesuai flag ungsian.</summary>
    public void SyncVisibility()
    {
        _game.World.EvacuatedNpcs.Clear();
        foreach (var npc in _game.Npcs)
        {
            var evacuated = IsEvacuated(npc.Info.Id);
            npc.Visible = !evacuated;
            if (evacuated) _game.World.EvacuatedNpcs.Add(npc.Info.Id);
        }
    }

    // ------------------------------------------------------------------ debug

    /// <summary>Debug F1: maju satu hari tanpa menunggu timer.</summary>
    public void DebugNextDay() => AdvanceDay();

    /// <summary>Debug F2: bakar satu situs pool bergilir (deterministik).</summary>
    public bool DebugIgnite()
    {
        if (_pool.Count == 0) return false;
        var index = (int)(WeatherSystem.Hash01((ulong)(Day * 41 + ++_debugIgnitions)) * _pool.Count) % _pool.Count;
        var site = _pool[index];
        return _game.Fire.IgniteCell(site.X, site.Y, _intensity);
    }

    /// <summary>Debug/tes: paksa nilai meter tanpa lewat simulasi.</summary>
    public void DebugSetMeters(float village, float forest)
    {
        VillageSafety = Math.Clamp(village, 0f, 100f);
        ForestHealth = Math.Clamp(forest, 0f, 100f);
        CountFires();
        EvaluateEvacuation();
        SyncVisibility();
    }
}
