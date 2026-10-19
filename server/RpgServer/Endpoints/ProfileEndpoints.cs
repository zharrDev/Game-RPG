namespace RpgServer;

/// <summary>Endpoint profil: daftar, buat, detail, hapus, resume.</summary>
public static class ProfileEndpoints
{
    public static void Map(WebApplication app)
    {
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
            if (profile is null) return ApiResults.NotFound(id);
            return Results.Ok(new { profile, slots = db.ListSlots(id), events = db.ListEvents(id, 10) });
        });

        app.MapDelete("/api/profiles/{id}", (GameDb db, string id) =>
            db.DeleteProfile(id) ? Results.Ok(new { deleted = id }) : ApiResults.NotFound(id));

        app.MapGet("/api/profiles/{id}/slots", (GameDb db, string id) =>
            db.ProfileExists(id) ? Results.Ok(new { slots = db.ListSlots(id) }) : ApiResults.NotFound(id));

        app.MapGet("/api/profiles/{id}/resume", (GameDb db, string id) =>
        {
            if (!db.ProfileExists(id)) return ApiResults.NotFound(id);
            var slots = db.ListSlots(id);
            if (slots.Count == 0)
                return Results.Ok(new { slot = (int?)null, state = (object?)null, info = (object?)null });
            var latest = slots.OrderByDescending(s => s.SavedAt, StringComparer.Ordinal).First();
            return Results.Ok(new { slot = latest.Slot, state = db.GetSlotState(id, latest.Slot), info = latest });
        });
    }
}
