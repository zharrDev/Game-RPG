using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RpgGame;

/// <summary>
/// Rantai misi data-driven (assets/data/objectives.json). Progres dicek dari
/// inventori, flag crafting, fog of war, atau pemadaman; misi selesai disimpan
/// sebagai flag "misi_&lt;id&gt;" sehingga ikut save lama tanpa format baru.
/// </summary>
public sealed class ObjectiveSystem
{
    public const string Path = "res://assets/data/objectives.json";
    public const string FlagPrefix = "misi_";

    public sealed class Objective
    {
        public string Id = "", Title = "", Hint = "", Type = "", Target = "", Next = "";
        public int Count = 1, RewardGold, RewardXp;
    }

    private readonly List<Objective> _objectives = new();
    private readonly Game _game;
    private string _start = "";
    private int _extinguishSession;

    public string ActiveId { get; private set; } = "";
    public string TrackerLine { get; private set; } = "";
    public int Version { get; private set; }
    public int CompletedCount => _objectives.Count(o => IsComplete(o));

    public ObjectiveSystem(Game game) => _game = game;

    public void Configure()
    {
        _objectives.Clear();
        using var doc = Assets.LoadJson(Path);
        var root = doc.RootElement;
        _start = root.TryGetProperty("start", out var start) ? start.GetString() ?? "" : "";
        foreach (var item in root.GetProperty("objectives").EnumerateArray())
        {
            _objectives.Add(new Objective
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Title = item.GetProperty("title").GetString() ?? "",
                Hint = item.TryGetProperty("hint", out var hint) ? hint.GetString() ?? "" : "",
                Type = item.GetProperty("type").GetString() ?? "",
                Target = item.TryGetProperty("target", out var target) ? target.GetString() ?? "" : "",
                Count = item.TryGetProperty("count", out var count) ? Math.Max(1, count.GetInt32()) : 1,
                Next = item.TryGetProperty("next", out var next) ? next.GetString() ?? "" : "",
                RewardGold = item.TryGetProperty("reward_gold", out var gold) ? gold.GetInt32() : 0,
                RewardXp = item.TryGetProperty("reward_xp", out var xp) ? xp.GetInt32() : 0,
            });
        }
        ActiveId = "";
        TrackerLine = "";
        Version++;
        Advance();
    }

    private Objective? Find(string id) => _objectives.FirstOrDefault(o => o.Id == id);

    private bool IsComplete(Objective objective) =>
        _game.Player is not null && _game.Player.State.Flags.GetValueOrDefault(FlagPrefix + objective.Id);

    /// <summary>Misi aktif = ikuti rantai dari start, lewati yang sudah selesai.</summary>
    private Objective? ResolveActive()
    {
        var current = Find(_start);
        var guard = 0;
        while (current is not null && IsComplete(current) && guard++ < 64)
            current = Find(current.Next);
        return current;
    }

    public void Advance()
    {
        if (_game.Player is null || _objectives.Count == 0) return;
        var active = ResolveActive();
        var activeId = active?.Id ?? "";
        if (activeId != ActiveId)
        {
            ActiveId = activeId;
            Version++;
        }

        if (active is not null)
        {
            var progress = Progress(active);
            if (progress >= active.Count) Complete(active);
            else
            {
                var line = $"{active.Title} {Math.Min(progress, active.Count)}/{active.Count}";
                if (line != TrackerLine)
                {
                    TrackerLine = line;
                    Version++;
                }
                return;
            }
        }

        // setelah selesai, tampilkan misi berikutnya pada frame yang sama
        active = ResolveActive();
        ActiveId = active?.Id ?? "";
        TrackerLine = active is null
            ? "Semua misi awal selesai — jelajahi Greenwoods."
            : BuildLine(active, Progress(active));
        Version++;
    }

    private static string BuildLine(Objective objective, int progress) =>
        $"{objective.Title} {Math.Min(progress, objective.Count)}/{objective.Count}";

    private int Progress(Objective objective)
    {
        var game = _game;
        if (game.Player is null) return 0;
        return objective.Type switch
        {
            "item" => game.Inventory?.Count(objective.Target) ?? 0,
            "craft" => game.Player.State.Flags.GetValueOrDefault($"crafted_{objective.Target}") ? 1 : 0,
            "explore" => (int)(game.Chunks?.ExploredPercent ?? 0),
            "extinguish" => _extinguishSession,
            _ => 0,
        };
    }

    private void Complete(Objective objective)
    {
        var game = _game;
        if (game.Player is null) return;
        game.Player.State.Flags[FlagPrefix + objective.Id] = true;
        if (objective.RewardGold > 0) game.Player.State.Gold += objective.RewardGold;
        if (objective.RewardXp > 0) game.Player.AddXp(objective.RewardXp);
        var reward = objective.RewardGold > 0 ? $" · +{objective.RewardGold} gold" : "";
        reward += objective.RewardXp > 0 ? $" · +{objective.RewardXp} XP" : "";
        game.Sfx?.Play("quest");
        game.Hud?.Toast($"Misi selesai: {objective.Title}{reward}");
        game.LogEvent("misi", objective.Id);
        game.Bus.RaiseQuestFlagSet(FlagPrefix + objective.Id);
        game.Autosave("misi selesai");
        Version++;
    }

    /// <summary>Dipanggil InventorySystem saat craft berhasil.</summary>
    public void NotifyCrafted(string recipeId)
    {
        _ = recipeId;
        Advance();
    }

    /// <summary>Dipanggil Game saat semprotan berhasil memadamkan api.</summary>
    public void NotifyExtinguished(int cells)
    {
        if (cells <= 0) return;
        _extinguishSession += cells;
        Advance();
    }

    public string ActiveHint => Find(ActiveId)?.Hint ?? "";
}
