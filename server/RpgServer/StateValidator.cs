using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace RpgServer;

/// <summary>
/// Menormalkan + memvalidasi payload save game sebelum ditulis ke SQLite.
/// Progresi (xp/level) bersifat client-authoritative karena ini game solo offline
/// tanpa auth; server hanya menjaga bentuk data & batas nilai agar DB tetap sehat.
/// </summary>
public static class StateValidator
{
    public const string CurrentVersion = "1";
    private const int MaxFlagEntries = 64;
    private const int MaxSkills = 16;

    public sealed record Result(bool Ok, string? Error, string? Json)
    {
        public static Result Fail(string error) => new(false, error, null);
        public static Result Pass(string json) => new(true, null, json);
    }

    private static readonly string[] SkillsAllowed = { "slash", "aura_slash", "heal_light" };

    // TypeInfoResolver harus eksplisit: options tanpa resolver akan ditolak saat
    // JsonNode.ToJsonString menandainya read-only.
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static Result Normalize(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return Result.Fail("state harus berupa objek JSON");

        try
        {
            var src = JsonNode.Parse(body.GetRawText())!.AsObject();
            var player = src["player"] as JsonObject ?? new JsonObject();
            var world = src["world"] as JsonObject ?? new JsonObject();
            var stats = src["stats"] as JsonObject ?? new JsonObject();

            var maxHp = ClampInt(GetNumber(player, "maxHp", 100), 10, 9999);
            var hp = ClampInt(GetNumber(player, "hp", maxHp), 0, maxHp);
            var level = ClampInt(GetNumber(player, "level", 1), 1, 99);
            var xp = ClampInt(GetNumber(player, "xp", 0), 0, 10_000_000);
            var skillLevel = ClampInt(GetNumber(player, "skillLevel", 0), 0, 3);

            var x = ClampDouble(GetNumber(player, "x", 0), -100_000, 1_000_000);
            var y = ClampDouble(GetNumber(player, "y", 0), -100_000, 100_000);
            var facing = GetNumber(player, "facing", 1) < 0 ? -1 : 1;

            var skills = new JsonArray();
            if (player["skills"] is JsonArray incoming)
            {
                foreach (var item in incoming)
                {
                    var name = item?.GetValue<string>();
                    if (name is null) continue;
                    if (!SkillsAllowed.Contains(name))
                        return Result.Fail($"skill tidak dikenal: {name}");
                    if (!skills.Any(n => n!.GetValue<string>() == name))
                        skills.Add(name);
                    if (skills.Count >= MaxSkills) break;
                }
            }

            var flags = NormalizeBoolMap(world["flags"] as JsonObject, "flags");
            if (flags is null) return Result.Fail("world.flags harus berisi nilai true/false");
            var defeated = NormalizeCountMap(world["defeated"] as JsonObject);
            if (defeated is null) return Result.Fail("world.defeated harus berisi angka >= 0");

            var state = new JsonObject
            {
                ["version"] = CurrentVersion,
                ["player"] = new JsonObject
                {
                    ["x"] = Math.Round(x, 2),
                    ["y"] = Math.Round(y, 2),
                    ["facing"] = facing,
                    ["hp"] = hp,
                    ["maxHp"] = maxHp,
                    ["level"] = level,
                    ["xp"] = xp,
                    ["skillLevel"] = skillLevel,
                    ["skills"] = skills,
                },
                ["world"] = new JsonObject
                {
                    ["zone"] = Slug(world["zone"]?.GetValue<string>(), "greenwood", 40),
                    ["checkpoint"] = Slug(world["checkpoint"]?.GetValue<string>(), "camp_start", 40),
                    ["flags"] = flags,
                    ["defeated"] = defeated,
                },
                ["stats"] = new JsonObject
                {
                    ["playtime"] = Math.Round(ClampDouble(GetNumber(stats, "playtime", 0), 0, 100_000_000), 1),
                    ["deaths"] = ClampInt(GetNumber(stats, "deaths", 0), 0, 10_000_000),
                    ["kills"] = ClampInt(GetNumber(stats, "kills", 0), 0, 10_000_000),
                },
                ["savedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };

            return Result.Pass(state.ToJsonString(PrettyJson));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Result.Fail($"payload tidak valid: {ex.Message}");
        }
    }

    private static JsonObject? NormalizeBoolMap(JsonObject? source, string label)
    {
        var result = new JsonObject();
        if (source is null) return result;
        foreach (var (key, value) in source)
        {
            if (result.Count >= MaxFlagEntries) break;
            if (!IsSlug(key, 60)) throw new InvalidOperationException($"{label}: nama '{key}' tidak valid");
            result[key] = value?.GetValueKind() == JsonValueKind.True;
        }
        return result;
    }

    private static JsonObject? NormalizeCountMap(JsonObject? source)
    {
        var result = new JsonObject();
        if (source is null) return result;
        foreach (var (key, value) in source)
        {
            if (result.Count >= MaxFlagEntries) break;
            if (!IsSlug(key, 60)) throw new InvalidOperationException($"defeated: nama '{key}' tidak valid");
            var count = value is null ? 0 : ClampInt(value.GetValue<double>(), 0, 1_000_000);
            result[key] = count;
        }
        return result;
    }

    private static bool IsSlug(string value, int maxLength) =>
        value.Length is > 0 && value.Length <= maxLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');

    private static string Slug(string? value, string fallback, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return IsSlug(trimmed, maxLength) ? trimmed : fallback;
    }

    private static double GetNumber(JsonObject obj, string key, double fallback)
    {
        if (obj[key] is not JsonValue value) return fallback;
        if (!value.TryGetValue<double>(out var number)) return fallback;
        return double.IsFinite(number) ? number : fallback;
    }

    private static int ClampInt(double value, int min, int max) => (int)Math.Clamp(Math.Round(value), min, max);

    private static double ClampDouble(double value, double min, double max) => Math.Clamp(value, min, max);
}
