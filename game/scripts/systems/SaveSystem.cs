using System.Linq;
using Godot;

namespace RpgGame;

/// <summary>
/// Semua jalur simpan/muat: state permainan, autosave, slot manual, profil,
/// resume, dan catatan perjalanan (journey). Server tetap sumber kebenaran
/// saat online; SaveApi yang memutuskan jatuh ke simpanan lokal.
/// </summary>
public sealed class SaveSystem
{
    private readonly Game _game;

    public SaveSystem(Game game) => _game = game;

    /// <summary>Catat peristiwa ke catatan perjalanan (dilakukan server, bukan client).</summary>
    public void LogEvent(string kind, string detail)
    {
        var game = _game;
        if (game.ProfileId is null) return;
        game.Api?.AddEvent(game.ProfileId, kind, detail);
    }

    // ------------------------------------------------------------- autosave

    /// <summary>Simpan otomatis; true kalau berhasil (server atau penyimpanan lokal).</summary>
    public bool Autosave(string reason)
    {
        var game = _game;
        if (game.ProfileId is null || game.Api is null || game.Player is null) return false;
        var info = game.Api.Save(game.ProfileId, game.CurrentSlot, BuildState());
        var target = game.Api.Offline ? "lokal" : $"slot {game.CurrentSlot}";
        if (info is not null) game.Hud?.Toast($"Tersimpan ({reason}) · {target}");
        else game.Hud?.Toast($"Gagal menyimpan ({reason}): {game.Api.LastError}");
        return info is not null;
    }

    /// <summary>Simpan sebelum game ditutup (tombol Keluar maupun jendela ditutup).</summary>
    public bool SaveOnExit()
    {
        if (_game.ProfileId is null) return false;
        _game.AutosaveTimer = 0;
        return Autosave("keluar");
    }

    // ---------------------------------------------------------- state & slot

    public GameState BuildState()
    {
        var game = _game;
        var state = game.Player!.State;
        state.X = game.Player.Position.X;
        state.Y = game.Player.Position.Y;
        state.Zone = game.ZoneId;
        state.Playtime = game.Playtime;
        return state;
    }

    public void SaveToSlot(int slot)
    {
        var game = _game;
        if (game.ProfileId is null || game.Api is null || game.Player is null) return;
        game.CurrentSlot = slot;
        var info = game.Api.Save(game.ProfileId, slot, BuildState());
        game.Hud?.Toast(info is null
            ? $"Gagal menyimpan slot {slot}: {game.Api.LastError}"
            : $"Perjalanan tersimpan di slot {slot}");
        if (info is not null) game.Hud?.SetJournal(game.Api.Journey(game.ProfileId));
    }

    public void LoadFromSlot(int slot)
    {
        var game = _game;
        if (game.ProfileId is null || game.Api is null) return;
        var state = game.Api.Load(game.ProfileId, slot);
        if (state is null)
        {
            game.Hud?.Toast($"Slot {slot} kosong.");
            return;
        }
        ApplyLoadedState(state);
        game.Hud?.Toast($"Lanjut dari slot {slot} — {state.Zone}");
        game.CloseMenus();
    }

    public void ApplyLoadedState(GameState state)
    {
        var game = _game;
        if (game.Player is null || game.World is null) return;
        game.Player.State.Hp = state.Hp;
        game.Player.State.MaxHp = state.MaxHp;
        game.Player.State.Level = state.Level;
        game.Player.State.Xp = state.Xp;
        game.Player.State.SkillLevel = state.SkillLevel;
        game.Player.State.Skills = state.Skills;
        game.Player.State.Zone = state.Zone;
        game.Player.State.Checkpoint = state.Checkpoint;
        game.Player.State.Flags = state.Flags;
        game.Player.State.Defeated = state.Defeated;
        game.Player.State.Gold = state.Gold;
        game.Player.State.Potions = state.Potions;
        game.Player.State.Deaths = state.Deaths;
        game.Player.State.Kills = state.Kills;
        game.Playtime = state.Playtime;
        game.Player.ApplyState(state);
        game.CloseDialog();
        // tampilkan peti yang sudah dibuka sesuai flag tersimpan
        foreach (var chest in game.World.Data.Chests)
        {
            if (game.ChestOpened(chest)) game.World.SetPropTile(chest.Col, chest.Row, "chest_open");
        }
        game.World.ZoneAt(game.Player.Position);
        game.ZoneId = state.Zone;
        game.ZoneName = game.World.Data.Zones.FirstOrDefault(z => z.Id == state.Zone)?.Name ?? state.Zone;
    }

    // --------------------------------------------------------------- profil

    public void ShowProfiles()
    {
        var game = _game;
        if (game.Hud is null || game.Api is null) return;
        game.Hud.ShowPauseMenu(false);
        game.Hud.ShowProfileMenu(true);
        var profiles = game.Api.ListProfiles();
        var status = profiles.Count > 0
            ? "Pilih profil di bawah, atau buat yang baru:"
            : "Belum ada profil. Tulis nama lalu tekan \"Buat profil\".";
        game.Hud.SetProfiles(profiles, status);
    }

    public void CreateProfile(string name)
    {
        var game = _game;
        if (game.Api is null || game.Hud is null) return;
        var profile = game.Api.CreateProfile(name);
        if (profile is null)
        {
            game.Hud.Toast($"Gagal membuat profil: {game.Api.LastError}");
            ShowProfiles();
            return;
        }
        StartProfile(profile.Id);
    }

    public void StartProfile(string profileId)
    {
        var game = _game;
        if (game.Api is null || game.Hud is null) return;
        game.ProfileId = profileId;
        var profiles = game.Api.ListProfiles();
        game.ProfileName = profiles.FirstOrDefault(p => p.Id == profileId)?.Name ?? profileId;
        game.Hud.ShowProfileMenu(false);
        game.Hud.SetJournal(game.Api.Journey(profileId));

        var resume = game.Api.Resume(profileId);
        if (resume is { } saved)
        {
            game.CurrentSlot = saved.Slot;
            ApplyLoadedState(saved.State);
            game.Hud.Toast($"Lanjut perjalanan {game.ProfileName} — slot {saved.Slot}");
        }
        else
        {
            game.Hud.Toast($"Petualangan baru dimulai, {game.ProfileName}. Tekan F untuk menyimpan.");
        }
    }
}
