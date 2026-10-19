using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RpgGame;

/// <summary>
/// Kejadian kebakaran data-driven (assets/data/incidents.json). Tiap insiden
/// dimulai saat pemain mendekat di zonanya: beberapa tile dibakar; setelah api
/// padam pemain mendapat barang bukti, lalu bisa mengonfrontasi tersangkanya.
/// </summary>
public sealed class IncidentSystem
{
    public const string Path = "res://assets/data/incidents.json";
    public const string FlagPrefix = "incident_";
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

    public IReadOnlyList<Incident> All => _incidents;
    public Incident? Active => _active;
    public string? ActiveId => _active?.Id;
    public bool Started => _active is not null && Flag(StartedFlag(_active));
    public bool Done => _active is not null && Flag(DoneFlag(_active));
    public int ClosedCases => _incidents.Count(i => Flag(CasePrefix + i.Id));
    public int DoneCount => _incidents.Count(i => Flag(DoneFlag(i)));
    public int EvidenceCount => EvidenceKinds().Count;
    public const string EndingFlag = "ending";

    public List<string> EvidenceKinds() => _incidents
        .Where(i => Flag(DoneFlag(i)) && i.Evidence.Length > 0)
        .Select(i => i.Evidence).Distinct().ToList();

    public static string StartedFlag(Incident incident) => $"{FlagPrefix}{incident.Id}_started";
    public static string DoneFlag(Incident incident) => $"{FlagPrefix}{incident.Id}_done";
    public static string CaseFlag(Incident incident) => $"{CasePrefix}{incident.Id}";

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

    public Incident? Find(string id) => _incidents.FirstOrDefault(i => i.Id == id);

    private bool Flag(string name) =>
        _game.Player is not null && _game.Player.State.Flags.GetValueOrDefault(name);

    private void SetFlag(string name)
    {
        if (_game.Player is not null) _game.Player.State.Flags[name] = true;
    }

    /// <summary>Mulai insiden tertentu (dipakai tes/debug); id kosong = insiden pertama.</summary>
    public bool Start(string id = "")
    {
        var incident = id.Length == 0 ? _active : Find(id);
        if (incident is null || _game.Player is null || _game.Fire is null) return false;
        _active = incident;
        SetFlag(StartedFlag(incident));
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
        if (game.Player is null || game.Fire is null) return;
        foreach (var incident in _incidents)
        {
            var started = Flag(StartedFlag(incident));
            var done = Flag(DoneFlag(incident));
            if (!started)
            {
                var distance = game.Player.Position.DistanceTo(Center(incident));
                if (game.ZoneId == incident.Zone && distance <= incident.DiscoverTiles * World.TileSize)
                    Start(incident.Id);
                continue;
            }
            if (done) continue;
            var radius = incident.RadiusTiles * World.TileSize;
            if (game.Fire.CountBurningNear(Center(incident), radius) > 0) continue;

            SetFlag(DoneFlag(incident));
            if (incident.Evidence.Length > 0) game.Inventory?.Add(incident.Evidence);
            var evidence = incident.EvidenceText.Length > 0 ? incident.EvidenceText : ItemCatalog.NameOf(incident.Evidence);
            game.Hud?.Toast($"Api {incident.Name} padam. Bukti ditemukan: {evidence}.");
            game.Sfx?.Play("quest");
            game.LogEvent("bukti", incident.Evidence);
            game.Objectives?.Advance();
            game.Autosave("bukti ditemukan");
        }
    }

    /// <summary>Dialog konfrontasi untuk tersangka (null = pakai dialog biasa).</summary>
    public IReadOnlyList<string>? TryConfront(NpcInfo info)
    {
        var game = _game;
        if (game.Player is null) return null;
        var incident = _incidents.FirstOrDefault(i => i.Suspect == info.Id &&
            Flag(DoneFlag(i)) && !Flag(CaseFlag(i)));
        if (incident is null) return null;

        _active = incident;
        SetFlag(CaseFlag(incident));
        if (incident.RewardGold > 0) game.Player.State.Gold += incident.RewardGold;
        if (incident.RewardXp > 0) game.Player.AddXp(incident.RewardXp);
        game.Sfx?.Play("quest");
        game.LogEvent("konfrontasi", incident.Suspect);
        game.Bus.RaiseQuestFlagSet(CaseFlag(incident));
        game.Objectives?.Advance();
        game.Autosave("konfrontasi");
        return incident.Confront;
    }

    /// <summary>
    /// Finale: serahkan berkas ke Mira setelah minimal satu bukti terkumpul.
    /// Tingkat ending mengikuti persentase bukti dari seluruh insiden.
    /// </summary>
    public bool TryFinale(NpcInfo info)
    {
        var game = _game;
        if (info.Id != "mira" || game.Player is null) return false;
        if (game.Player.State.Flags.GetValueOrDefault(EndingFlag)) return false;
        if (game.Player.State.Flags.GetValueOrDefault("quest_slime_done") == false &&
            info.Quest is not null) return false;
        if (DoneCount <= 0) return false;

        var percent = _incidents.Count == 0 ? 0f : EvidenceCount * 100f / _incidents.Count;
        string tier, title, body;
        if (percent < 40f)
        {
            tier = "dalang_lolos";
            title = "ENDING — Dalang Lolos";
            body = "Api padam, tetapi Rangga menghilang bersama arang terakhir.\nBukti yang tersisa tak cukup untuk menyeretnya ke pengadilan.";
        }
        else if (percent < 80f)
        {
            tier = "tertangkap";
            title = "ENDING — Tertangkap";
            body = "Rangga tertangkap di jembatan timur. Penyewa tanah dari luar desa lolos,\ntetapi Greenwood bisa bernapas lagi.";
        }
        else
        {
            tier = "hutan_pulih";
            title = "ENDING — Hutan Pulih";
            body = "Semua bukti menunjuk Rangga. Hutan Greenwoods pulih:\ntunas-tunas baru tumbuh di tanah bekas terbakar.";
        }
        game.Player.State.Flags[EndingFlag] = true;
        game.Player.State.Flags[$"ending_{tier}"] = true;
        game.LogEvent("ending", tier);
        game.Autosave("finale");
        game.ShowEnding(title, body);
        return true;
    }

    public string StatusOf(Incident incident)
    {
        if (Flag(CaseFlag(incident))) return "kasus ditutup";
        if (Flag(DoneFlag(incident))) return "bukti didapat, cari tersangka";
        if (Flag(StartedFlag(incident))) return "api menyala";
        return "belum ditemukan";
    }

    /// <summary>Skor kecurigaan per warga dari insiden yang menunjuk dirinya.</summary>
    public int SuspicionScore(string npcId) => _incidents.Count(i => i.Suspect == npcId &&
        (Flag(DoneFlag(i)) || Flag(CaseFlag(i))));

    /// <summary>Teks papan bukti (M7) untuk panel HUD "V".</summary>
    public string BoardText()
    {
        var lines = new List<string> { "PAPAN BUKTI — Pos Pantau", "" };
        foreach (var incident in _incidents)
        {
            var suspect = _game.World?.Data.Npcs.FirstOrDefault(n => n.Id == incident.Suspect)?.Name ?? incident.Suspect;
            lines.Add($"• {incident.Name} [{StatusOf(incident)}] — tersangka: {suspect}");
        }
        lines.Add("");
        lines.Add("BUKTI TERKUMPUL:");
        var evidence = _game.Player?.State.Inventory
            .Where(p => ItemCatalog.Get(p.Key)?.Kind == "evidence" && p.Value > 0).OrderBy(p => p.Key).ToList()
            ?? new List<KeyValuePair<string, int>>();
        if (evidence.Count == 0) lines.Add("  (belum ada — padamkan api insiden)");
        foreach (var (id, count) in evidence)
            lines.Add($"  - {ItemCatalog.NameOf(id)} x{count}");
        lines.Add("");
        lines.Add("TERSANGKA:");
        if (_game.World is not null)
        {
            foreach (var npc in _game.World.Data.Npcs)
            {
                var score = SuspicionScore(npc.Id);
                if (score <= 0) continue;
                lines.Add($"  - {npc.Name}: {score} petunjuk");
            }
        }
        lines.Add("");
        lines.Add($"Tanah dipulihkan: {_game.Player?.State.Restored ?? 0} tunas");
        return string.Join("\n", lines);
    }
}
