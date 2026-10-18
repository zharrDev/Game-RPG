using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Godot;

namespace RpgGame;

/// <summary>
/// Simpanan cadangan di mesin sendiri (default `user://save_offline.json`) dipakai
/// saat backend C# tidak bisa dihubungi, supaya game tetap bisa dimainkan dan
/// progres tidak hilang. Bentuk data sengaja meniru API server agar kode game
/// tidak perlu tahu bedanya.
/// </summary>
public sealed class LocalSaveStore
{
    public const string DefaultPath = "user://save_offline.json";

    private readonly string _path;

    public LocalSaveStore(string path = DefaultPath) => _path = path;

    public string Path => _path;

    private static JsonObject Empty() => new()
    {
        ["profiles"] = new JsonArray(),
        ["events"] = new JsonObject(),
    };

    private JsonObject Read()
    {
        if (!FileAccess.FileExists(_path)) return Empty();
        var text = FileAccess.GetFileAsString(_path);
        if (string.IsNullOrWhiteSpace(text)) return Empty();
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? Empty();
        }
        catch (JsonException)
        {
            GD.PrintErr($"[offline] file simpanan rusak, diabaikan: {_path}");
            return Empty();
        }
    }

    private void Write(JsonObject root)
    {
        using var file = FileAccess.Open(_path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PrintErr($"[offline] gagal menulis {_path}: {FileAccess.GetOpenError()}");
            return;
        }
        // JsonNode.ToJsonString menolak options tanpa resolver (lihat StateValidator di server)
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        file.StoreString(root.ToJsonString(options));
    }

    private static JsonArray Profiles(JsonObject root)
    {
        if (root["profiles"] is JsonArray profiles) return profiles;
        var created = new JsonArray();
        root["profiles"] = created;
        return created;
    }

    private static JsonObject? Find(JsonObject root, string profileId) =>
        Profiles(root).OfType<JsonObject>().FirstOrDefault(p => (string?)p["id"] == profileId);

    private static string Now() => DateTime.UtcNow.ToString("o");

    public List<ProfileInfo> ListProfiles()
    {
        var result = new List<ProfileInfo>();
        foreach (var node in Profiles(Read()).OfType<JsonObject>())
        {
            result.Add(new ProfileInfo
            {
                Id = (string?)node["id"] ?? "",
                Name = (string?)node["name"] ?? "",
                CreatedAt = (string?)node["createdAt"] ?? "",
                UpdatedAt = (string?)node["savedAt"] ?? (string?)node["createdAt"] ?? "",
            });
        }
        return result.OrderByDescending(p => p.UpdatedAt, StringComparer.Ordinal).ToList();
    }

    public ProfileInfo? CreateProfile(string name)
    {
        var root = Read();
        var profiles = Profiles(root);
        var next = 1;
        foreach (var existing in profiles.OfType<JsonObject>())
        {
            var id = (string?)existing["id"] ?? "";
            if (id.StartsWith("local-", StringComparison.Ordinal) &&
                int.TryParse(id["local-".Length..], out var number))
                next = Math.Max(next, number + 1);
        }
        var profile = new JsonObject
        {
            ["id"] = $"local-{next}",
            ["name"] = name,
            ["createdAt"] = Now(),
            ["slots"] = new JsonObject(),
        };
        profiles.Add(profile);
        Write(root);
        return new ProfileInfo { Id = $"local-{next}", Name = name, CreatedAt = (string?)profile["createdAt"] ?? "" };
    }

    public bool DeleteProfile(string profileId)
    {
        var root = Read();
        var profiles = Profiles(root);
        var target = Find(root, profileId);
        if (target is null) return false;
        profiles.Remove(target);
        if (root["events"] is JsonObject events) events.Remove(profileId);
        Write(root);
        return true;
    }

    private static JsonObject Slots(JsonObject? profile)
    {
        if (profile is null) return new JsonObject();
        if (profile["slots"] is JsonObject slots) return slots;
        var created = new JsonObject();
        profile["slots"] = created;
        return created;
    }

    public SlotInfo? Save(string profileId, int slot, GameState state)
    {
        var root = Read();
        var profile = Find(root, profileId);
        if (profile is null) return null;
        var slots = Slots(profile);
        var savedAt = Now();
        var payload = state.ToJson();
        slots[$"{slot}"] = new JsonObject { ["savedAt"] = savedAt, ["state"] = payload };
        profile["savedAt"] = savedAt;
        Write(root);
        return Describe(slot, savedAt, payload);
    }

    private static SlotInfo Describe(int slot, string savedAt, JsonObject? state)
    {
        state ??= new JsonObject();
        var player = state["player"] as JsonObject ?? new JsonObject();
        var world = state["world"] as JsonObject ?? new JsonObject();
        var stats = state["stats"] as JsonObject ?? new JsonObject();
        static int Int(JsonObject? node, string key, int fallback) =>
            node?[key] is JsonValue value && value.TryGetValue<int>(out var parsed) ? parsed : fallback;
        static double Dbl(JsonObject? node, string key) =>
            node?[key] is JsonValue value && value.TryGetValue<double>(out var parsed) ? parsed : 0;
        return new SlotInfo
        {
            Slot = slot,
            SavedAt = savedAt,
            Zone = (string?)world["zone"] ?? "greenwood_vale",
            Level = Int(player, "level", 1),
            Playtime = Dbl(stats, "playtime"),
            Kills = Int(stats, "kills", 0),
        };
    }

    public List<SlotInfo> ListSlots(string profileId)
    {
        var root = Read();
        var slots = Slots(Find(root, profileId));
        var result = new List<SlotInfo>();
        foreach (var (key, value) in slots)
        {
            if (!int.TryParse(key, out var slot) || value is not JsonObject entry) continue;
            result.Add(Describe(slot, (string?)entry["savedAt"] ?? "", entry["state"] as JsonObject));
        }
        return result.OrderBy(s => s.Slot).ToList();
    }

    public GameState? Load(string profileId, int slot)
    {
        var root = Read();
        var entry = Slots(Find(root, profileId))[$"{slot}"] as JsonObject;
        if (entry?["state"] is not JsonObject state) return null;
        using var document = JsonDocument.Parse(state.ToJsonString());
        return GameState.FromJson(document.RootElement);
    }

    public (int Slot, GameState State)? Resume(string profileId)
    {
        var latest = ListSlots(profileId).OrderByDescending(s => s.SavedAt, StringComparer.Ordinal).FirstOrDefault();
        if (latest is null) return null;
        var state = Load(profileId, latest.Slot);
        return state is null ? null : (latest.Slot, state);
    }

    public List<JournalEntry> Journey(string profileId, int limit = 12)
    {
        var root = Read();
        var result = new List<JournalEntry>();
        if (root["events"] is not JsonObject events) return result;
        if (events[profileId] is not JsonArray list) return result;
        foreach (var node in list.OfType<JsonObject>().Reverse())
        {
            result.Add(new JournalEntry
            {
                Kind = (string?)node["kind"] ?? "",
                Detail = (string?)node["detail"] ?? "",
                CreatedAt = (string?)node["createdAt"] ?? "",
            });
            if (result.Count >= limit) break;
        }
        return result;
    }

    /// <summary>Hapus file simpanan lokal (dipakai setelah semua profil dipindah ke server).</summary>
    public bool DeleteFile()
    {
        if (!FileAccess.FileExists(_path)) return true;
        return DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(_path)) == Error.Ok;
    }

    public void AddEvent(string profileId, string kind, string detail)
    {
        var root = Read();
        if (root["events"] is not JsonObject events)
        {
            events = new JsonObject();
            root["events"] = events;
        }
        if (events[profileId] is not JsonArray list)
        {
            list = new JsonArray();
            events[profileId] = list;
        }
        list.Add(new JsonObject { ["kind"] = kind, ["detail"] = detail, ["createdAt"] = Now() });
        while (list.Count > 60) list.RemoveAt(0);
        Write(root);
    }
}
