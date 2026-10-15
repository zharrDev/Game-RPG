using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using RpgServer;

// ---------------------------------------------------------------------------- //
// Konfigurasi
// ---------------------------------------------------------------------------- //
var repoRoot = LocateRepoRoot();
var port = ResolvePort(args);
var host = Environment.GetEnvironmentVariable("RPG_HOST") ?? "127.0.0.1";
var dbPath = Environment.GetEnvironmentVariable("RPG_DB") ?? Path.Combine(repoRoot, "server", "data", "rpg.db");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = repoRoot,
    WebRootPath = Path.Combine(repoRoot, "web"),
});
builder.WebHost.UseUrls($"http://{host}:{port}");
builder.Services.AddSingleton(new GameDb(dbPath));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.WriteIndented = true);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
var assetsDir = Path.Combine(repoRoot, "assets");
if (Directory.Exists(assetsDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(assetsDir),
        RequestPath = "/assets",
    });
}

// ---------------------------------------------------------------------------- //
// API
// ---------------------------------------------------------------------------- //
app.MapGet("/api/health", (GameDb db) => Results.Ok(new
{
    status = "ok",
    version = "1.0.0",
    character = "arga",
    database = db.DatabasePath,
    slots = GameDb.SlotCount,
    serverTime = DateTime.UtcNow.ToString("o"),
}));

app.MapGet("/api/profiles", (GameDb db) => Results.Ok(new { profiles = db.ListProfiles() }));

app.MapPost("/api/profiles", (GameDb db, ProfileRequest request) =>
{
    var name = (request.Name ?? string.Empty).Trim();
    if (name.Length is < 1 or > 24)
        return Results.BadRequest(new { error = "nama profil harus 1-24 karakter" });

    var id = db.CreateProfile(name);
    db.AddEvent(id, "profile_created", name);
    return Results.Created($"/api/profiles/{id}", new { profile = db.ListProfiles().First(p => p.Id == id) });
});

app.MapGet("/api/profiles/{id}", (GameDb db, string id) =>
{
    var profile = db.ListProfiles().FirstOrDefault(p => p.Id == id);
    if (profile is null) return NotFound(id);
    return Results.Ok(new { profile, slots = db.ListSlots(id), events = db.ListEvents(id, 10) });
});

app.MapDelete("/api/profiles/{id}", (GameDb db, string id) =>
    db.DeleteProfile(id) ? Results.Ok(new { deleted = id }) : NotFound(id));

app.MapGet("/api/profiles/{id}/slots", (GameDb db, string id) =>
    db.ProfileExists(id) ? Results.Ok(new { slots = db.ListSlots(id) }) : NotFound(id));

app.MapGet("/api/profiles/{id}/resume", (GameDb db, string id) =>
{
    if (!db.ProfileExists(id)) return NotFound(id);
    var slots = db.ListSlots(id);
    if (slots.Count == 0) return Results.Ok(new { slot = (int?)null, state = (object?)null, info = (object?)null });
    var latest = slots.OrderByDescending(s => s.SavedAt, StringComparer.Ordinal).First();
    return Results.Ok(new { slot = latest.Slot, state = db.GetSlotState(id, latest.Slot), info = latest });
});

app.MapGet("/api/profiles/{id}/slots/{slot:int}", (GameDb db, string id, int slot) =>
{
    if (!db.ProfileExists(id)) return NotFound(id);
    if (!IsValidSlot(slot)) return BadSlot();
    var state = db.GetSlotState(id, slot);
    return state is null
        ? Results.NotFound(new { error = $"slot {slot} masih kosong" })
        : Results.Ok(new { slot, info = db.GetSlot(id, slot), state });
});

app.MapPut("/api/profiles/{id}/slots/{slot:int}", (GameDb db, string id, int slot, JsonElement state) =>
{
    if (!db.ProfileExists(id)) return NotFound(id);
    if (!IsValidSlot(slot)) return BadSlot();

    var validation = StateValidator.Normalize(state);
    if (!validation.Ok) return Results.BadRequest(new { error = validation.Error });

    var previous = db.GetSlotState(id, slot)?.ToJsonString();
    var info = db.SaveSlot(id, slot, validation.Json!);
    var journey = db.LogJourneyProgress(id, previous, validation.Json!);
    return Results.Ok(new { saved = true, slot, info, events = journey.Take(5) });
});

app.MapDelete("/api/profiles/{id}/slots/{slot:int}", (GameDb db, string id, int slot) =>
{
    if (!db.ProfileExists(id)) return NotFound(id);
    if (!IsValidSlot(slot)) return BadSlot();
    return db.DeleteSlot(id, slot)
        ? Results.Ok(new { deleted = slot })
        : Results.NotFound(new { error = $"slot {slot} masih kosong" });
});

app.MapPost("/api/profiles/{id}/events", (GameDb db, string id, EventRequest request) =>
{
    if (!db.ProfileExists(id)) return NotFound(id);
    var kind = (request.Kind ?? string.Empty).Trim();
    var detail = (request.Detail ?? string.Empty).Trim();
    if (kind.Length is < 1 or > 40) return Results.BadRequest(new { error = "kind harus 1-40 karakter" });
    db.AddEvent(id, kind, detail.Length > 200 ? detail[..200] : detail);
    return Results.Ok(new { events = db.ListEvents(id, 10) });
});

app.MapGet("/api/profiles/{id}/journey", (GameDb db, string id) =>
{
    var profile = db.ListProfiles().FirstOrDefault(p => p.Id == id);
    if (profile is null) return NotFound(id);
    return Results.Ok(new
    {
        profile,
        slots = db.ListSlots(id),
        events = db.ListEvents(id, 30),
    });
});

app.MapFallbackToFile("index.html");

app.Logger.LogInformation("RPG server siap di http://{Host}:{Port} (db: {Db})", host, port, dbPath);
app.Run();
return 0;

// ---------------------------------------------------------------------------- //
// Helper
// ---------------------------------------------------------------------------- //
static IResult NotFound(string id) => Results.NotFound(new { error = $"profil '{id}' tidak ditemukan" });
static IResult BadSlot() => Results.BadRequest(new { error = $"slot harus 1-{GameDb.SlotCount}" });
static bool IsValidSlot(int slot) => slot >= 1 && slot <= GameDb.SlotCount;

static int ResolvePort(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed)) return parsed;
        if (args[i].StartsWith("--port=", StringComparison.Ordinal) && int.TryParse(args[i][7..], out var inline)) return inline;
    }
    return int.TryParse(Environment.GetEnvironmentVariable("RPG_PORT"), out var envPort) ? envPort : 5080;
}

/// <summary>Cari folder root project (yang berisi web/index.html) dari lokasi binary.</summary>
static string LocateRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "web", "index.html"))) return current.FullName;
        current = current.Parent;
    }
    // fallback: naik 4 level dari bin/Debug/net8.0/ (server/RpgServer/bin/Debug/net8.0)
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}

internal sealed record ProfileRequest(string? Name);
internal sealed record EventRequest(string? Kind, string? Detail);
