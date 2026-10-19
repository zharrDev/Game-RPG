namespace RpgServer;

/// <summary>Endpoint catatan perjalanan (journey) + event manual.</summary>
public static class JourneyEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/profiles/{id}/events", (GameDb db, string id, EventRequest request) =>
        {
            if (!db.ProfileExists(id)) return ApiResults.NotFound(id);
            var kind = (request.Kind ?? string.Empty).Trim();
            var detail = (request.Detail ?? string.Empty).Trim();
            if (kind.Length is < 1 or > 40)
                return Results.BadRequest(new { error = "kind harus 1-40 karakter" });
            db.AddEvent(id, kind, detail.Length > 200 ? detail[..200] : detail);
            return Results.Ok(new { events = db.ListEvents(id, 10) });
        });

        app.MapGet("/api/profiles/{id}/journey", (GameDb db, string id) =>
        {
            var profile = db.ListProfiles().FirstOrDefault(p => p.Id == id);
            if (profile is null) return ApiResults.NotFound(id);
            return Results.Ok(new
            {
                profile,
                slots = db.ListSlots(id),
                events = db.ListEvents(id, 30),
            });
        });
    }
}
