using System.Threading.RateLimiting;
using Microsoft.Extensions.FileProviders;
using RpgServer;

// ---------------------------------------------------------------------------- //
// Bootstrap: opsi -> DB (backup + schema) -> middleware -> endpoint
// ---------------------------------------------------------------------------- //
var repoRoot = ServerOptions.LocateRepoRoot();
var options = ServerOptions.Resolve(args, repoRoot);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = repoRoot,
});
builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
builder.Services.AddSingleton(new GameDb(options.DbPath));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.WriteIndented = true);

// Rate limiter mutasi: klien liar tidak boleh membanjiri SQLite. Limit longgar
// supaya tes API yang menembak cepat tetap lolos.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var isMutation = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
        return isMutation
            ? RateLimitPartition.GetFixedWindowLimiter("mutasi", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter("baca");
    });
});

var app = builder.Build();

// Backup + schema: salinan DB lama dibuat sebelum skema bisa berubah.
var dbExisted = File.Exists(options.DbPath);
var db = app.Services.GetRequiredService<GameDb>();
if (dbExisted && db.BackupTo(options.DbPath + ".bak"))
    app.Logger.LogInformation("backup DB -> {Backup}", options.DbPath + ".bak");
app.Logger.LogInformation("skema DB v{Version}", db.ReadSchemaVersion());

app.UseRateLimiter();
ServerOptions.ServeFolder(app, Path.Combine(repoRoot, "game", "assets"), "/assets"); // aset game
ServerOptions.ServeFolder(app, Path.Combine(repoRoot, "assets"), "/frames");        // frame AI + atlas lama
ServerOptions.ServeFolder(app, Path.Combine(repoRoot, "docs"), "/preview");         // pratinjau PNG

HealthEndpoints.Map(app, repoRoot);
ProfileEndpoints.Map(app);
SaveEndpoints.Map(app);
JourneyEndpoints.Map(app);

app.Logger.LogInformation("RPG server siap di http://{Host}:{Port} (db: {Db})",
    options.Host, options.Port, options.DbPath);
app.Run();
return 0;

/// <summary>Opsi runtime: port/host/db dari argumen, env, lalu appsettings.</summary>
internal sealed record ServerOptions(string Host, int Port, string DbPath)
{
    public static ServerOptions Resolve(string[] args, string repoRoot)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile(Path.Combine("server", "RpgServer", "appsettings.json"), optional: true)
            .Build();

        var host = Environment.GetEnvironmentVariable("RPG_HOST")
                   ?? config["Rpg:Host"] ?? "127.0.0.1";
        int? envPort = int.TryParse(Environment.GetEnvironmentVariable("RPG_PORT"), out var fromEnv) ? fromEnv : null;
        int? cfgPort = int.TryParse(config["Rpg:Port"], out var fromConfig) ? fromConfig : null;
        var port = ResolvePort(args) ?? envPort ?? cfgPort ?? 5080;
        var configuredDb = Environment.GetEnvironmentVariable("RPG_DB") ?? config["Rpg:DatabasePath"];
        var dbPath = string.IsNullOrWhiteSpace(configuredDb)
            ? Path.Combine(repoRoot, "server", "data", "rpg.db")
            : (Path.IsPathRooted(configuredDb) ? configuredDb : Path.Combine(repoRoot, configuredDb));
        return new ServerOptions(host, port, dbPath);
    }

    private static int? ResolvePort(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed)) return parsed;
            if (args[i].StartsWith("--port=", StringComparison.Ordinal) && int.TryParse(args[i][7..], out var inline)) return inline;
        }
        return null;
    }

    public static void ServeFolder(WebApplication app, string folder, string requestPath)
    {
        if (!Directory.Exists(folder)) return;
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(folder),
            RequestPath = requestPath,
        });
    }

    /// <summary>Cari folder root project (yang berisi proyek Godot game/project.godot) dari lokasi binary.</summary>
    public static string LocateRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "game", "project.godot"))) return current.FullName;
            current = current.Parent;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
