using System.Text.Json;
using RpgServer;

// Harness tes konsol tanpa NuGet: assertion sederhana + exit code.
// Dijalankan oleh scripts/test-all.sh langkah 3b.
var passed = 0;
var failed = new List<string>();

void Check(string name, bool condition, string detail = "")
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"ok    {name}");
    }
    else
    {
        failed.Add($"{name}{(detail.Length > 0 ? $" — {detail}" : "")}");
        Console.WriteLine($"FAIL  {name}{(detail.Length > 0 ? $" — {detail}" : "")}");
    }
}

static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

static string ValidState(int hp = 80, int level = 1) => $$"""
{
  "version": "1",
  "player": { "hp": {{hp}}, "maxHp": 100, "level": {{level}}, "xp": 0, "gold": 5, "potions": 1 },
  "world": { "zone": "greenwood_vale", "checkpoint": "village_square", "flags": { "quest_mira": false } },
  "stats": { "kills": 0, "deaths": 0, "playtime": 0 }
}
""";

Console.WriteLine("== StateValidator ==");
var okState = StateValidator.Normalize(Json(ValidState()));
Check("state valid diterima", okState.Ok && okState.Json is not null, okState.Error ?? "");
Check("version dinormalkan ke 2",
    okState.Json is not null && JsonDocument.Parse(okState.Json).RootElement.GetProperty("version").GetString() == "2");

var migrated = okState.Json is null ? default : JsonDocument.Parse(okState.Json).RootElement.GetProperty("world");
Check("save v1 dimigrasikan: hari 1 & meter penuh",
    migrated.GetProperty("day").GetInt32() == 1 &&
    Math.Abs(migrated.GetProperty("villageSafety").GetDouble() - 100) < 0.01 &&
    Math.Abs(migrated.GetProperty("forestHealth").GetDouble() - 100) < 0.01);

var clampedMeters = StateValidator.Normalize(Json("""
{ "version": "2", "world": { "day": 0, "villageSafety": 150, "forestHealth": -20 } }
"""));
var metersJson = clampedMeters.Ok ? JsonDocument.Parse(clampedMeters.Json!).RootElement.GetProperty("world") : default;
Check("meter & hari dijepit (day 1, meter 0..100)",
    clampedMeters.Ok && metersJson.GetProperty("day").GetInt32() == 1 &&
    Math.Abs(metersJson.GetProperty("villageSafety").GetDouble() - 100) < 0.01 &&
    Math.Abs(metersJson.GetProperty("forestHealth").GetDouble() - 0) < 0.01,
    clampedMeters.Error ?? "");

var clampedHp = StateValidator.Normalize(Json(ValidState(hp: 999)));
Check("hp di atas maxHp dijepit",
    clampedHp.Ok && JsonDocument.Parse(clampedHp.Json!).RootElement.GetProperty("player").GetProperty("hp").GetInt32() == 100);

var clampedLevel = StateValidator.Normalize(Json(ValidState(level: 500)));
Check("level dijepit ke 99",
    clampedLevel.Ok && JsonDocument.Parse(clampedLevel.Json!).RootElement.GetProperty("player").GetProperty("level").GetInt32() == 99);

Check("non-objek ditolak", !StateValidator.Normalize(Json("\"bukan state\"")).Ok);
Check("skill tak dikenal ditolak", !StateValidator.Normalize(Json("""
{ "player": { "skills": ["pedang_ajaib"] } }
""")).Ok);

var inventoryState = StateValidator.Normalize(Json("""
{ "version": "1", "player": { "hp": 50 }, "world": { "inventory": { "kain": 3, "tali": 999 } } }
"""));
var inventoryJson = inventoryState.Ok ? JsonDocument.Parse(inventoryState.Json!).RootElement.GetProperty("world").GetProperty("inventory") : default;
Check("inventory dinormalkan (jepit 99)",
    inventoryState.Ok && inventoryJson.GetProperty("kain").GetInt32() == 3 &&
    inventoryJson.GetProperty("tali").GetInt32() == 99,
    inventoryState.Error ?? "");
Check("id barang tidak valid ditolak", !StateValidator.Normalize(Json("""
{ "version": "1", "world": { "inventory": { "id aneh": 1 } } }
""")).Ok);
Check("inventory bukan angka ditolak", !StateValidator.Normalize(Json("""
{ "version": "1", "world": { "inventory": { "kain": "tiga" } } }
""")).Ok);

Console.WriteLine("== GameDb ==");
var dbPath = Path.Combine(Path.GetTempPath(), $"rpg-unit-{Guid.NewGuid():n}.db");
var backupPath = dbPath + ".bak";
try
{
    var db = new GameDb(dbPath);
    Check("DB dibuat di disk", File.Exists(dbPath));
    Check("schema_version = 2", db.ReadSchemaVersion() == GameDb.SchemaVersion, $"{db.ReadSchemaVersion()}");

    var profileId = db.CreateProfile("Unit Tester");
    Check("profil dibuat & terdaftar", db.ProfileExists(profileId) && db.ListProfiles().Any(p => p.Id == profileId));

    var saved = db.SaveSlot(profileId, 1, JsonDocument.Parse(ValidState()).RootElement.GetRawText());
    Check("slot tersimpan", saved is not null && saved.Slot == 1 && saved.Hp == 80);
    Check("slot bisa dibaca kembali", db.GetSlotState(profileId, 1) is not null);
    Check("resume menemukan slot terakhir", db.ListSlots(profileId).Count == 1);

    db.AddEvent(profileId, "unit_test", "halo");
    Check("event tercatat", db.ListEvents(profileId, 5).Any(e => e.Kind == "unit_test"));

    Check("backup VACUUM INTO berhasil", db.BackupTo(backupPath) && File.Exists(backupPath));
    Check("hapus slot", db.DeleteSlot(profileId, 1) && db.ListSlots(profileId).Count == 0);
    Check("hapus profil", db.DeleteProfile(profileId) && !db.ProfileExists(profileId));
}
finally
{
    foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", backupPath })
        if (File.Exists(path)) File.Delete(path);
}

Console.WriteLine();
Console.WriteLine($"=== hasil unit server: {passed} lulus, {failed.Count} gagal ===");
foreach (var failure in failed) Console.WriteLine($"  GAGAL: {failure}");
return failed.Count == 0 ? 0 : 1;
