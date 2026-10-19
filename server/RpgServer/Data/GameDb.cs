using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace RpgServer;

public sealed record ProfileSummary(
    string Id,
    string Name,
    string CreatedAt,
    string LastSeenAt,
    double Playtime,
    int Level,
    int Xp,
    string Zone,
    string Checkpoint,
    int Deaths,
    int Bosses,
    int SlotCount);

public sealed record SlotInfo(
    int Slot,
    string SavedAt,
    int Level,
    int Xp,
    string Zone,
    string Checkpoint,
    double Playtime,
    int Hp,
    int MaxHp,
    int Deaths,
    int Kills);

public sealed record JourneyEvent(long Id, string Kind, string Detail, string CreatedAt);

/// <summary>Penyimpanan profil, slot save, dan catatan perjalanan (SQLite).</summary>
public sealed class GameDb
{
    public const int SlotCount = 3;

    /// <summary>Versi skema DB. Naikkan bersama migrasi (Save v2 di M8).</summary>
    public const int SchemaVersion = 1;

    public string DatabasePath { get; }
    private readonly string _connectionString;

    public GameDb(string path)
    {
        DatabasePath = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        Initialize();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS profiles (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                created_at  TEXT NOT NULL,
                last_seen_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS save_slots (
                profile_id  TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
                slot        INTEGER NOT NULL,
                state       TEXT NOT NULL,
                saved_at    TEXT NOT NULL,
                PRIMARY KEY (profile_id, slot)
            );
            CREATE TABLE IF NOT EXISTS events (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id  TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
                kind        TEXT NOT NULL,
                detail      TEXT NOT NULL,
                created_at  TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_events_profile ON events(profile_id, id DESC);
            CREATE TABLE IF NOT EXISTS meta (
                key         TEXT PRIMARY KEY,
                value       TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        // schema_version disimpan di DB supaya migrasi M8 bisa dibedakan per-file.
        using var meta = connection.CreateCommand();
        meta.CommandText = """
            INSERT INTO meta (key, value) VALUES ('schema_version', $version)
            ON CONFLICT(key) DO NOTHING;
            """;
        meta.Parameters.AddWithValue("$version", SchemaVersion.ToString());
        meta.ExecuteNonQuery();
    }

    /// <summary>Versi skema yang tercatat di DB (untuk endpoint health & migrasi).</summary>
    public int ReadSchemaVersion()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        return int.TryParse(command.ExecuteScalar() as string, out var version) ? version : 0;
    }

    /// <summary>
    /// Salin DB ke <c>&lt;path&gt;.bak</c> memakai VACUUM INTO (aman saat WAL aktif,
    /// menghasilkan file SQLite utuh). Dipanggil sekali saat startup sebelum
    /// perubahan skema; kegagalan backup tidak boleh menggagalkan server.
    /// </summary>
    public bool BackupTo(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $path";
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    // ---------------------------------------------------------------- profiles

    public bool ProfileExists(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM profiles WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is not null;
    }

    public string CreateProfile(string name)
    {
        var id = Guid.NewGuid().ToString("n")[..12];
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO profiles (id, name, created_at, last_seen_at) VALUES ($id, $name, $now, $now)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", Now());
        command.ExecuteNonQuery();
        return id;
    }

    public bool DeleteProfile(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM profiles WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public List<ProfileSummary> ListProfiles()
    {
        var result = new List<ProfileSummary>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, created_at, last_seen_at FROM profiles ORDER BY last_seen_at DESC";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var latest = GetLatestSlot(connection, id);
            result.Add(new ProfileSummary(
                id,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                latest?.Info.Playtime ?? 0,
                latest?.Info.Level ?? 1,
                latest?.Info.Xp ?? 0,
                latest?.Info.Zone ?? "greenwood",
                latest?.Info.Checkpoint ?? "camp_start",
                latest?.Info.Deaths ?? 0,
                CountBosses(latest?.State),
                CountSlots(connection, id)));
        }
        return result;
    }

    private static int CountSlots(SqliteConnection connection, string profileId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM save_slots WHERE profile_id = $id";
        command.Parameters.AddWithValue("$id", profileId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int CountBosses(string? stateJson)
    {
        if (stateJson is null) return 0;
        var node = JsonNode.Parse(stateJson);
        if (node?["world"]?["flags"] is not JsonObject flags) return 0;
        return flags.Count(f => f.Key.StartsWith("boss_", StringComparison.Ordinal) && f.Value?.GetValueKind() == JsonValueKind.True);
    }

    private sealed record LatestSlot(SlotInfo Info, string State);

    private static LatestSlot? GetLatestSlot(SqliteConnection connection, string profileId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT slot, state, saved_at FROM save_slots WHERE profile_id = $id ORDER BY saved_at DESC LIMIT 1";
        command.Parameters.AddWithValue("$id", profileId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var state = reader.GetString(1);
        var info = DescribeSlot(reader.GetInt32(0), state, reader.GetString(2));
        return info is null ? null : new LatestSlot(info, state);
    }

    // ------------------------------------------------------------------ slots

    public List<SlotInfo> ListSlots(string profileId)
    {
        var result = new List<SlotInfo>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT slot, state, saved_at FROM save_slots WHERE profile_id = $id ORDER BY slot ASC";
        command.Parameters.AddWithValue("$id", profileId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var info = DescribeSlot(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
            if (info is not null) result.Add(info);
        }
        return result;
    }

    public SlotInfo? GetSlot(string profileId, int slot)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT slot, state, saved_at FROM save_slots WHERE profile_id = $id AND slot = $slot";
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$slot", slot);
        using var reader = command.ExecuteReader();
        return reader.Read() ? DescribeSlot(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public JsonNode? GetSlotState(string profileId, int slot)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM save_slots WHERE profile_id = $id AND slot = $slot";
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$slot", slot);
        var raw = command.ExecuteScalar() as string;
        return raw is null ? null : JsonNode.Parse(raw);
    }

    private static SlotInfo? DescribeSlot(int slot, string stateJson, string savedAt)
    {
        var node = JsonNode.Parse(stateJson);
        if (node is null) return null;
        static int Int(JsonNode? n, int fallback) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;
        static double Dbl(JsonNode? n, double fallback) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : fallback;
        static string Str(JsonNode? n, string fallback) => n?.GetValue<string>() ?? fallback;

        var player = node["player"];
        var world = node["world"];
        var stats = node["stats"];
        return new SlotInfo(
            slot,
            savedAt,
            Int(player?["level"], 1),
            Int(player?["xp"], 0),
            Str(world?["zone"], "greenwood"),
            Str(world?["checkpoint"], "camp_start"),
            Dbl(stats?["playtime"], 0),
            Int(player?["hp"], 100),
            Int(player?["maxHp"], 100),
            Int(stats?["deaths"], 0),
            Int(stats?["kills"], 0));
    }

    public SlotInfo SaveSlot(string profileId, int slot, string normalizedState)
    {
        var now = Now();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO save_slots (profile_id, slot, state, saved_at) VALUES ($id, $slot, $state, $now)
            ON CONFLICT(profile_id, slot) DO UPDATE SET state = excluded.state, saved_at = excluded.saved_at;
            UPDATE profiles SET last_seen_at = $now WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$slot", slot);
        command.Parameters.AddWithValue("$state", normalizedState);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
        return DescribeSlot(slot, normalizedState, now)!;
    }

    public bool DeleteSlot(string profileId, int slot)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM save_slots WHERE profile_id = $id AND slot = $slot";
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$slot", slot);
        return command.ExecuteNonQuery() > 0;
    }

    // ----------------------------------------------------------------- events

    public void AddEvent(string profileId, string kind, string detail)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO events (profile_id, kind, detail, created_at) VALUES ($id, $kind, $detail, $now)";
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$detail", detail);
        command.Parameters.AddWithValue("$now", Now());
        command.ExecuteNonQuery();
    }

    public List<JourneyEvent> ListEvents(string profileId, int limit = 30)
    {
        var result = new List<JourneyEvent>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, kind, detail, created_at FROM events WHERE profile_id = $id ORDER BY id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$id", profileId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new JourneyEvent(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return result;
    }

    /// <summary>Bandingkan save lama vs baru, lalu catat perjalanan yang berubah.</summary>
    public List<JourneyEvent> LogJourneyProgress(string profileId, string? previousStateJson, string newStateJson)
    {
        var before = previousStateJson is null ? null : JsonNode.Parse(previousStateJson);
        var after = JsonNode.Parse(newStateJson);
        if (after is null) return new List<JourneyEvent>();

        var added = new List<(string Kind, string Detail)>();

        var levelBefore = (before?["player"]?["level"])?.GetValue<int>() ?? 1;
        var levelAfter = (after["player"]?["level"])?.GetValue<int>() ?? 1;
        if (levelAfter > levelBefore) added.Add(("level_up", $"{levelBefore} -> {levelAfter}"));

        var checkpointBefore = (before?["world"]?["checkpoint"])?.GetValue<string>();
        var checkpointAfter = (after["world"]?["checkpoint"])?.GetValue<string>();
        if (checkpointAfter is not null && checkpointAfter != checkpointBefore)
            added.Add(("checkpoint", checkpointAfter));

        var zoneBefore = (before?["world"]?["zone"])?.GetValue<string>();
        var zoneAfter = (after["world"]?["zone"])?.GetValue<string>();
        if (zoneAfter is not null && zoneAfter != zoneBefore) added.Add(("zone", zoneAfter));

        if (before?["world"]?["flags"] is JsonObject oldFlags && after["world"]?["flags"] is JsonObject newFlags)
        {
            foreach (var (key, value) in newFlags)
            {
                var wasTrue = oldFlags[key]?.GetValueKind() == JsonValueKind.True;
                var isTrue = value?.GetValueKind() == JsonValueKind.True;
                if (isTrue && !wasTrue) added.Add(("flag", key));
            }
        }

        var deathsBefore = (before?["stats"]?["deaths"])?.GetValue<int>() ?? 0;
        var deathsAfter = (after["stats"]?["deaths"])?.GetValue<int>() ?? 0;
        if (deathsAfter > deathsBefore) added.Add(("death", $"{deathsAfter - deathsBefore}x"));

        var killsBefore = (before?["stats"]?["kills"])?.GetValue<int>() ?? 0;
        var killsAfter = (after["stats"]?["kills"])?.GetValue<int>() ?? 0;
        if (killsAfter > killsBefore) added.Add(("kills", $"+{killsAfter - killsBefore}"));

        foreach (var (kind, detail) in added) AddEvent(profileId, kind, detail);
        return ListEvents(profileId, 20);
    }
}
