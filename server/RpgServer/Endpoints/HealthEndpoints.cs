using System.Text.Json;

namespace RpgServer;

/// <summary>Endpoint kesehatan: dipakai klien untuk memutuskan online/offline.</summary>
public static class HealthEndpoints
{
    public const string Version = "1.0.0";

    public static void Map(WebApplication app, string repoRoot)
    {
        app.MapGet("/", () => Results.Ok(new
        {
            status = "ok",
            game = "Arga: Open World RPG",
            engine = "godot",
            engineVersion = "Godot 4 (.NET/C#)",
            frontend = Path.Combine(repoRoot, "game"),
            run = "bash scripts/dev.sh   # backend ini + jendela game Godot",
            note = "Tidak ada frontend HTML: server ini murni API save game.",
            slots = GameDb.SlotCount,
            api = new[]
            {
                "/api/health",
                "/api/profiles",
                "/api/profiles/{id}",
                "/api/profiles/{id}/slots/{slot}",
                "/api/profiles/{id}/resume",
                "/api/profiles/{id}/journey",
            },
            assets = "/assets/",
            frames = "/frames/",
            preview = "/preview/preview-world.png",
        }));

        app.MapGet("/api/health", (GameDb db) => Results.Ok(new
        {
            status = "ok",
            version = Version,
            character = "arga",
            schemaVersion = GameDb.SchemaVersion,
            database = db.DatabasePath,
            slots = GameDb.SlotCount,
            serverTime = DateTime.UtcNow.ToString("o"),
        }));
    }
}
