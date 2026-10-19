using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RpgGame;

/// <summary>
/// Kejadian kebakaran data-driven (assets/data/incidents.json). Satu insiden
/// dimulai saat pemain mendekat: beberapa tile dibakar, lalu setelah api padam
/// pemain mendapat barang bukti dan bisa mengonfrontasi tersangka.
/// </summary>
public sealed class IncidentSystem
{
    public const string Path = "res://assets/data/incidents.json";
    public const string StartedPrefix = "incident_";
    public const string DonePrefix = "incident_";
    public const string CasePrefix = "kasus_";

    public sealed class Incident
    {
        public string Id = "", Name = "", Zone = "", Evidence = "", EvidenceText = "", Suspect = "";
        public int Col, Row, RadiusTiles = 5, RewardGold, RewardXp;
        public float DiscoverTiles = 18;
        public readonly List<(int Col, int Row)> Fires = new();
        public readonly List<string> Confront = new();
    }

    private readonly Game _game;
    private readonly List<Incident> _incidents = new();
    private Incident? _active;

    public IncidentSystem(Game game) => _game = game;

    public string? ActiveId => _active?.Id;
    public Incident? Active => _active;
    public bool Started => _active is not null && Flag(StartedPrefix + _active.Id + "_started");
    public bool Done => _active is not null && Flag(DonePrefix + _active.Id + "_done");

    public void Configure()
    {
        _incidents.Clear();
        using var doc = Assets.LoadJson(Path);
        foreach (var item in doc.RootElement.GetProperty("incidents").EnumerateArray())
        {
            var incident = new Incident
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Name = item.GetProperty("name").GetString() ?? "",
                Zone = item.GetProperty("zone").GetString() ?? "",
                Col = item.GetProperty("col").GetInt32(),
                Row = item.GetProperty("row").GetInt32(),
                RadiusTiles = item.TryGetProperty("radius_tiles", out var radius) ? radius.GetInt32() : 5,
                DiscoverTiles = item.TryGetProperty("discover_tiles", out var discover) ? discover.GetSingle() : 18f,
                Evidence = item.GetProperty("evidence").GetString() ?? "",
                EvidenceText = item.TryGetProperty("evidence_text", out var etext) ? etext.GetString() ?? "" : "",
                Suspect = item.GetProperty("suspect").GetString() ?? "",
                RewardGold = item.TryGetProperty("reward_gold", out var gold) ? gold.GetInt32() : 0,
                RewardXp = item.TryGetProperty("reward_xp", out var xp) ? xp.GetInt32() : 0,
            };
            foreach (var fire in item.GetProperty("fires").EnumerateArray())
                incident.Fires.Add((fire[0].GetInt32(), fire[1].GetInt32()));
            if (item.TryGetProperty("confront", out var lines))
                foreach (var line in lines.EnumerateArray())
                    incident.Confront.Add(line.GetString() ?? "");
            _incidents.Add(incident);
        }
        _active = _incidents.FirstOrDefault();
    }

    public Vector2 Center(Incident incident) =>
        new((incident.Col + 0.5f) * World.TileSize, (incident.Row + 0.5f) * World.TileSize);

    private bool Flag(string name) =>
        _game.Player is not null && _game.Player.State.Flags.GetValueOrDefault(name);

    private void SetFlag(string name)
    {
        if (_game.Player is not null) _game.Player.State.Flags[name] = true;
    }

    /// <summary>Mulai insiden tertentu (dipakai tes/debug); id kosong = insiden pertama.</summary>
    public bool Start(string id = "")
    {
        var incident = id.Length == 0 ? _active : _incidents.FirstOrDefault(i => i.Id == id);
        if (incident is null || _game.Player is null || _game.Fire is null) return false;
        _active = incident;
        SetFlag(StartedPrefix + incident.Id + "_started");
        foreach (var (col, row) in incident.Fires) _game.Fire.IgniteCell(col, row, 0.85f);
        _game.Hud?.Toast($"Asap mengepul dari {incident.Name}! Api menjalar — R untuk menyemprot.");
        _game.Sfx?.Play("hurt", -8f);
        _game.Feel?.Shake(2.4f);
        _game.LogEvent("kebakaran", incident.Id);
        _game.Autosave("api mulai");
        return true;
    }

    public void Advance()
    {
        var game = _game;
        if (game.Player is null || game.Fire is null || _active is null) return;
        var incident = _active;
        var started = Flag(StartedPrefix + incident.Id + "_started");
        var done = Flag(DonePrefix + incident.Id + "_done");

        if (!started)
        {
            var distance = game.Player.Position.DistanceTo(Center(incident));
            if (game.ZoneId == incident.Zone && distance <= incident.DiscoverTiles * World.TileSize)
                Start(incident.Id);
            return;
        }
        if (done) return;

        var radius = incident.RadiusTiles * World.TileSize;
        if (game.Fire.CountBurningNear(Center(incident), radius) > 0) return;

        SetFlag(DonePrefix + incident.Id + "_done");
        if (incident.Evidence.Length > 0) game.Inventory?.Add(incident.Evidence);
        var evidence = incident.EvidenceText.Length > 0 ? incident.EvidenceText : ItemCatalog.NameOf(incident.Evidence);
        game.Hud?.Toast($"Api Pos Loji padam. Bukti ditemukan: {evidence}.");
        game.Sfx?.Play("quest");
        game.LogEvent("bukti", incident.Evidence);
        game.Objectives?.Advance();
        game.Autosave("bukti ditemukan");
    }

    /// <summary>Dialog konfrontasi untuk tersangka (null = pakai dialog biasa).</summary>
    public IReadOnlyList<string>? TryConfront(NpcInfo info)
    {
        var game = _game;
        if (_active is null || game.Player is null) return null;
        var incident = _active;
        if (info.Id != incident.Suspect) return null;
        if (!Flag(DonePrefix + incident.Id + "_done")) return null;
        if (Flag(CasePrefix + incident.Id)) return null;

        SetFlag(CasePrefix + incident.Id);
        if (incident.RewardGold > 0) game.Player.State.Gold += incident.RewardGold;
        if (incident.RewardXp > 0) game.Player.AddXp(incident.RewardXp);
        game.Sfx?.Play("quest");
        game.LogEvent("konfrontasi", incident.Suspect);
        game.Bus.RaiseQuestFlagSet(CasePrefix + incident.Id);
        game.Objectives?.Advance();
        game.Autosave("konfrontasi");
        return incident.Confront;
    }
}
