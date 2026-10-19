using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace RpgGame;

public sealed class ProfileInfo
{
    public string Id = "";
    public string Name = "";
    public string CreatedAt = "";
    public string UpdatedAt = "";
}

public sealed class SlotInfo
{
    public int Slot;
    public string SavedAt = "";
    public string Zone = "";
    public int Level = 1;
    public double Playtime;
    public int Kills;
}

public sealed class JournalEntry
{
    public string Kind = "";
    public string Detail = "";
    public string CreatedAt = "";
}

/// <summary>State yang disimpan; bentuknya harus cocok dengan StateValidator di server.</summary>
public sealed class GameState
{
    public double X, Y;
    public int Facing = 1;               // -1 kiri, 1 kanan
    public int Hp = 100, MaxHp = 100, Level = 1, Xp, SkillLevel;
    public List<string> Skills = new() { "slash" };
    public string Zone = "greenwood_vale";
    public string Checkpoint = "village_square";
    public Dictionary<string, bool> Flags = new();
    public Dictionary<string, int> Defeated = new();
    public List<int> Explored = new();
    public double Playtime;
    public int Deaths, Kills;
    public int Gold, Potions;

    public JsonObject ToJson()
    {
        var skills = new JsonArray();
        foreach (var skill in Skills) skills.Add(skill);
        var flags = new JsonObject();
        foreach (var (key, value) in Flags) flags[key] = value;
        var defeated = new JsonObject();
        foreach (var (key, value) in Defeated) defeated[key] = value;
        var explored = new JsonArray();
        foreach (var index in Explored) explored.Add(index);
        return new JsonObject
        {
            ["player"] = new JsonObject
            {
                ["x"] = Math.Round(X, 2),
                ["y"] = Math.Round(Y, 2),
                ["facing"] = Facing,
                ["hp"] = Hp,
                ["maxHp"] = MaxHp,
                ["level"] = Level,
                ["xp"] = Xp,
                ["skillLevel"] = SkillLevel,
                ["skills"] = skills,
                ["gold"] = Gold,
                ["potions"] = Potions,
            },
            ["world"] = new JsonObject
            {
                ["zone"] = Zone,
                ["checkpoint"] = Checkpoint,
                ["flags"] = flags,
                ["defeated"] = defeated,
                ["explored"] = explored,
            },
            ["stats"] = new JsonObject
            {
                ["playtime"] = Math.Round(Playtime, 1),
                ["deaths"] = Deaths,
                ["kills"] = Kills,
            },
        };
    }

    public static GameState FromJson(JsonElement root)
    {
        var state = new GameState();
        if (!root.TryGetProperty("player", out var player)) return state;
        state.X = GetDouble(player, "x");
        state.Y = GetDouble(player, "y");
        state.Facing = GetInt(player, "facing", 1);
        state.Hp = GetInt(player, "hp", 100);
        state.MaxHp = GetInt(player, "maxHp", 100);
        state.Level = GetInt(player, "level", 1);
        state.Xp = GetInt(player, "xp");
        state.SkillLevel = GetInt(player, "skillLevel");
        state.Gold = GetInt(player, "gold");
        state.Potions = GetInt(player, "potions");
        if (player.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
        {
            state.Skills = skills.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => s.Length > 0).ToList();
        }
        if (root.TryGetProperty("world", out var world))
        {
            state.Zone = world.TryGetProperty("zone", out var z) ? z.GetString() ?? state.Zone : state.Zone;
            state.Checkpoint = world.TryGetProperty("checkpoint", out var c) ? c.GetString() ?? state.Checkpoint : state.Checkpoint;
            if (world.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Object)
                foreach (var flag in flags.EnumerateObject()) state.Flags[flag.Name] = flag.Value.GetBoolean();
            if (world.TryGetProperty("defeated", out var defeated) && defeated.ValueKind == JsonValueKind.Object)
                foreach (var mob in defeated.EnumerateObject()) state.Defeated[mob.Name] = mob.Value.GetInt32();
            if (world.TryGetProperty("explored", out var explored) && explored.ValueKind == JsonValueKind.Array)
                foreach (var chunk in explored.EnumerateArray())
                    if (chunk.ValueKind == JsonValueKind.Number) state.Explored.Add(chunk.GetInt32());
        }
        if (root.TryGetProperty("stats", out var stats))
        {
            state.Playtime = GetDouble(stats, "playtime");
            state.Deaths = GetInt(stats, "deaths");
            state.Kills = GetInt(stats, "kills");
        }
        return state;
    }

    private static double GetDouble(JsonElement obj, string key, double fallback = 0) =>
        obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : fallback;

    private static int GetInt(JsonElement obj, string key, int fallback = 0) =>
        obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : fallback;
}

/// <summary>
/// Klien REST ke backend ASP.NET Core (server/RpgServer). Panggilan dijalankan di
/// thread pool lalu ditunggu sinkron: main loop Godot memasang SynchronizationContext,
/// jadi menunggu langsung di main thread bisa deadlock.
/// </summary>
public sealed class SaveApi
{
    private readonly System.Net.Http.HttpClient _http;
    private readonly LocalSaveStore _local;

    public string BaseUrl { get; }
    public string? LastError { get; private set; }

    /// <summary>True = backend tidak bisa dihubungi; semua baca/tulis pindah ke penyimpanan lokal.</summary>
    public bool Offline { get; private set; }

    public SaveApi(string baseUrl, LocalSaveStore? local = null)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _local = local ?? new LocalSaveStore();
        _http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(6) };
    }

    public LocalSaveStore LocalStore => _local;

    private T? Run<T>(Func<T> action)
    {
        try
        {
            return Task.Run(action).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var cause = ex.InnerException ?? ex;
            LastError = cause.Message;
            // Hanya masalah jaringan yang berarti "server mati". Penolakan HTTP
            // (mis. 400 payload tidak valid) tidak boleh mengalihkan ke simpan lokal.
            if (cause is System.Net.Http.HttpRequestException or TaskCanceledException)
                Offline = true;
            GD.PrintErr($"[save] {LastError}");
            return default;
        }
    }

    private string Get(string path) => _http.GetStringAsync($"{BaseUrl}{path}").GetAwaiter().GetResult();

    public bool Healthy(out string info)
    {
        info = "";
        var body = Run(() => Get("/api/health"));
        if (body is null)
        {
            Offline = true;
            info = "offline";
            return false;
        }
        Offline = false;
        using var doc = JsonDocument.Parse(body);
        info = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        return true;
    }

    public List<ProfileInfo> ListProfiles()
    {
        var result = new List<ProfileInfo>();
        if (Offline) return _local.ListProfiles();
        var body = Run(() => Get("/api/profiles"));
        if (body is null) return Offline ? _local.ListProfiles() : result;
        using var doc = JsonDocument.Parse(body);
        foreach (var item in doc.RootElement.GetProperty("profiles").EnumerateArray())
        {
            result.Add(new ProfileInfo
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Name = item.GetProperty("name").GetString() ?? "",
                CreatedAt = item.TryGetProperty("createdAt", out var c) ? c.GetString() ?? "" : "",
                UpdatedAt = item.TryGetProperty("updatedAt", out var u) ? u.GetString() ?? "" : "",
            });
        }
        return result;
    }

    public ProfileInfo? CreateProfile(string name)
    {
        if (Offline) return _local.CreateProfile(name);
        var body = Run(() =>
        {
            var payload = new StringContent(new JsonObject { ["name"] = name }.ToJsonString(),
                Encoding.UTF8, "application/json");
            var response = _http.PostAsync($"{BaseUrl}/api/profiles", payload).GetAwaiter().GetResult();
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(text);
            return text;
        });
        if (body is null) return Offline ? _local.CreateProfile(name) : null;
        using var doc = JsonDocument.Parse(body);
        var profile = doc.RootElement.GetProperty("profile");
        return new ProfileInfo
        {
            Id = profile.GetProperty("id").GetString() ?? "",
            Name = profile.GetProperty("name").GetString() ?? "",
        };
    }

    public bool DeleteProfile(string profileId)
    {
        if (Offline) return _local.DeleteProfile(profileId);
        var ok = Run(() =>
        {
            var response = _http.DeleteAsync($"{BaseUrl}/api/profiles/{profileId}").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        });
        return ok || (Offline && _local.DeleteProfile(profileId));
    }

    public List<SlotInfo> ListSlots(string profileId)
    {
        var result = new List<SlotInfo>();
        if (Offline) return _local.ListSlots(profileId);
        var body = Run(() => Get($"/api/profiles/{profileId}/slots"));
        if (body is null) return Offline ? _local.ListSlots(profileId) : result;
        using var doc = JsonDocument.Parse(body);
        foreach (var item in doc.RootElement.GetProperty("slots").EnumerateArray())
            result.Add(ParseSlot(item));
        return result;
    }

    private static SlotInfo ParseSlot(JsonElement item) => new()
    {
        Slot = item.TryGetProperty("slot", out var s) ? s.GetInt32() : 0,
        SavedAt = item.TryGetProperty("savedAt", out var sa) ? sa.GetString() ?? "" : "",
        Zone = item.TryGetProperty("zone", out var z) ? z.GetString() ?? "" : "",
        Level = item.TryGetProperty("level", out var l) ? l.GetInt32() : 1,
        Playtime = item.TryGetProperty("playtime", out var p) ? p.GetDouble() : 0,
        Kills = item.TryGetProperty("kills", out var k) ? k.GetInt32() : 0,
    };

    public SlotInfo? Save(string profileId, int slot, GameState state)
    {
        if (Offline) return _local.Save(profileId, slot, state);
        var body = Run(() =>
        {
            var payload = new StringContent(state.ToJson().ToJsonString(), Encoding.UTF8, "application/json");
            var response = _http.PutAsync($"{BaseUrl}/api/profiles/{profileId}/slots/{slot}", payload).GetAwaiter().GetResult();
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{response.StatusCode}: {text}");
            return text;
        });
        if (body is null) return Offline ? _local.Save(profileId, slot, state) : null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("info", out var info) ? ParseSlot(info) : null;
    }

    public GameState? Load(string profileId, int slot)
    {
        if (Offline) return _local.Load(profileId, slot);
        var body = Run(() => Get($"/api/profiles/{profileId}/slots/{slot}"));
        if (body is null) return Offline ? _local.Load(profileId, slot) : null;
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("state", out var state)) return null;
        return GameState.FromJson(state);
    }

    public (int Slot, GameState State)? Resume(string profileId)
    {
        if (Offline) return _local.Resume(profileId);
        var body = Run(() => Get($"/api/profiles/{profileId}/resume"));
        if (body is null) return Offline ? _local.Resume(profileId) : null;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("slot", out var slot) || slot.ValueKind != JsonValueKind.Number) return null;
        if (!root.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Object) return null;
        return (slot.GetInt32(), GameState.FromJson(state));
    }

    public List<JournalEntry> Journey(string profileId, int limit = 12)
    {
        var result = new List<JournalEntry>();
        if (Offline) return _local.Journey(profileId, limit);
        var body = Run(() => Get($"/api/profiles/{profileId}/journey"));
        if (body is null) return Offline ? _local.Journey(profileId, limit) : result;
        using var doc = JsonDocument.Parse(body);
        foreach (var item in doc.RootElement.GetProperty("events").EnumerateArray())
        {
            result.Add(new JournalEntry
            {
                Kind = item.GetProperty("kind").GetString() ?? "",
                Detail = item.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "",
                CreatedAt = item.TryGetProperty("createdAt", out var c) ? c.GetString() ?? "" : "",
            });
            if (result.Count >= limit) break;
        }
        return result;
    }

    /// <summary>
    /// Salin profil + slot yang dibuat saat offline ke server (dipanggil sekali saat
    /// backend hidup lagi). Data lokal yang sudah dipindah lalu dihapus supaya tidak
    /// tersalin dua kali. Mengembalikan jumlah profil yang diimpor.
    /// </summary>
    public int SyncOfflineToServer(LocalSaveStore store)
    {
        if (Offline) return 0;
        var localProfiles = store.ListProfiles();
        if (localProfiles.Count == 0) return 0;
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in ListProfiles()) existing.Add(profile.Name);

        var imported = 0;
        foreach (var local in localProfiles)
        {
            if (existing.Contains(local.Name)) continue;
            var created = CreateProfile(local.Name);
            if (created is null) continue;
            foreach (var slot in store.ListSlots(local.Id))
            {
                var state = store.Load(local.Id, slot.Slot);
                if (state is null) continue;
                Save(created.Id, slot.Slot, state);
            }
            store.DeleteProfile(local.Id);
            imported++;
        }
        if (store.ListProfiles().Count == 0) store.DeleteFile();
        return imported;
    }

    public void AddEvent(string profileId, string kind, string detail)
    {
        if (Offline)
        {
            _local.AddEvent(profileId, kind, detail);
            return;
        }
        Run(() =>
        {
            var payload = new StringContent(new JsonObject { ["kind"] = kind, ["detail"] = detail }.ToJsonString(),
                Encoding.UTF8, "application/json");
            var response = _http.PostAsync($"{BaseUrl}/api/profiles/{profileId}/events", payload).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        });
    }
}
