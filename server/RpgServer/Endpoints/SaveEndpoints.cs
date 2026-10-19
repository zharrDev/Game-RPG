using System.Text.Json;

namespace RpgServer;

/// <summary>Endpoint slot save: baca, tulis (dengan validasi), hapus.</summary>
public static class SaveEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/profiles/{id}/slots/{slot:int}", (GameDb db, string id, int slot) =>
        {
            if (!db.ProfileExists(id)) return ApiResults.NotFound(id);
            if (!ApiResults.IsValidSlot(slot)) return ApiResults.BadSlot();
            var state = db.GetSlotState(id, slot);
            return state is null
                ? Results.NotFound(new { error = $"slot {slot} masih kosong" })
                : Results.Ok(new { slot, info = db.GetSlot(id, slot), state });
        });

        app.MapPut("/api/profiles/{id}/slots/{slot:int}", (GameDb db, string id, int slot, JsonElement state) =>
        {
            if (!db.ProfileExists(id)) return ApiResults.NotFound(id);
            if (!ApiResults.IsValidSlot(slot)) return ApiResults.BadSlot();

            var validation = StateValidator.Normalize(state);
            if (!validation.Ok) return Results.BadRequest(new { error = validation.Error });

            var previous = db.GetSlotState(id, slot)?.ToJsonString();
            var info = db.SaveSlot(id, slot, validation.Json!);
            var journey = db.LogJourneyProgress(id, previous, validation.Json!);
            return Results.Ok(new { saved = true, slot, info, events = journey.Take(5) });
        });

        app.MapDelete("/api/profiles/{id}/slots/{slot:int}", (GameDb db, string id, int slot) =>
        {
            if (!db.ProfileExists(id)) return ApiResults.NotFound(id);
            if (!ApiResults.IsValidSlot(slot)) return ApiResults.BadSlot();
            return db.DeleteSlot(id, slot)
                ? Results.Ok(new { deleted = slot })
                : Results.NotFound(new { error = $"slot {slot} masih kosong" });
        });
    }
}
