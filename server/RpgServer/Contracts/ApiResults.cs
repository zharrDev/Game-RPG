namespace RpgServer;

/// <summary>Bentuk hasil error bersama: selalu <c>{ error: "..." }</c> (kontrak tes API).</summary>
public static class ApiResults
{
    public static IResult NotFound(string id) =>
        Results.NotFound(new { error = $"profil '{id}' tidak ditemukan" });

    public static IResult BadSlot() =>
        Results.BadRequest(new { error = $"slot harus 1-{GameDb.SlotCount}" });

    public static bool IsValidSlot(int slot) => slot >= 1 && slot <= GameDb.SlotCount;
}
