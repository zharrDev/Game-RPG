using System;
using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>
/// Orkestrasi HUD & menu: update stat per frame, tracker quest, ringkasan
/// pause, dan tutup menu. Semua hanya membaca state pemain — tidak mengubah
/// permainan, kecuali mendelegasikan aksi ke sistem lain lewat Game.
/// </summary>
public sealed class UiManager
{
    private readonly Game _game;
    private int _questTrackerSignature = -1;

    public UiManager(Game game) => _game = game;

    /// <summary>Dipanggil setiap frame dari Game._Process (HUD selalu hidup).</summary>
    public void UpdateHud()
    {
        var game = _game;
        if (game.Player is null || game.World is null || game.Hud is null) return;
        game.Hud.UpdateStats(game.Player.State, game.Player.Energy, game.Playtime, game.ZoneName, game.Player.State.Checkpoint);
        game.Hud.UpdateMinimap(game.Player.Position, game.World.Data.PixelSize);
        game.Hud.UpdateClock(game.Lighting?.TimeOfDay ?? 0.5f);
        RefreshQuestTracker();
    }

    public void OpenPauseMenu()
    {
        var game = _game;
        if (game.Hud is null || game.Player is null) return;
        var time = TimeSpan.FromSeconds(game.Playtime);
        game.Hud.SetPauseSummary($"{game.ProfileName} · Lv {game.Player.State.Level} · {game.Player.State.Gold} gold · " +
            $"bunuh {game.Player.State.Kills} · tumbang {game.Player.State.Deaths} · {time.Minutes:00}:{time.Seconds:00} · " +
            $"{game.ZoneName} · peta {game.Chunks.ExploredPercent:0}%");
        game.Hud.ShowPauseMenu(true);
        if (game.ProfileId is not null && game.Api is not null) game.Hud.SetJournal(game.Api.Journey(game.ProfileId));
    }

    public void CloseMenus()
    {
        _game.Hud?.ShowPauseMenu(false);
        _game.Hud?.ShowProfileMenu(false);
    }

    /// <summary>
    /// Tracker quest di HUD (tahap 2g, murni informasi): quest yang belum
    /// selesai dihitung dari state pemain. Dipanggil tiap frame tetapi hanya
    /// membangun ulang teks kalau jumlah kill/flag berubah (cache signature).
    /// </summary>
    public void RefreshQuestTracker()
    {
        var game = _game;
        if (game.Player is null || game.World is null || game.Hud is null) return;
        var defeatedTotal = 0;
        foreach (var pair in game.Player.State.Defeated) defeatedTotal += pair.Value;
        var signature = game.Player.State.Kills * 7 + game.Player.State.Flags.Count * 131 + defeatedTotal * 17;
        if (signature == _questTrackerSignature) return;
        _questTrackerSignature = signature;

        var lines = new List<string>();
        foreach (var npc in game.World.Data.Npcs)
        {
            var quest = npc.Quest;
            if (quest is null || quest.Flag.Length == 0) continue;
            if (game.Player.State.Flags.GetValueOrDefault(quest.Flag)) continue;
            var killed = Math.Min(game.Player.State.Defeated.GetValueOrDefault(quest.Target), quest.Count);
            lines.Add(killed >= quest.Count
                ? $"- {npc.Name}: selesai, temui dia"
                : $"- {npc.Name}: kalahkan {quest.Target} {killed}/{quest.Count}");
        }
        game.Hud.SetQuestTracker(lines.Count == 0 ? "" : "QUEST\n" + string.Join("\n", lines));
    }
}
