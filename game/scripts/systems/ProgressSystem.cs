using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>
/// Progres pemain: XP/level, hitungan kill, hadiah quest, dan status quest
/// yang belum selesai (penanda "!"). Mengirim event lewat EventBus supaya HUD
/// dan sistem lain tidak perlu tahu detail di sini.
/// </summary>
public sealed class ProgressSystem
{
    private readonly Game _game;

    public ProgressSystem(Game game) => _game = game;

    /// <summary>True kalau NPC ini punya quest yang belum selesai (penanda "!").</summary>
    public bool QuestPending(NpcInfo info)
    {
        var quest = info.Quest;
        if (quest is null || quest.Flag.Length == 0 || _game.Player is null) return false;
        return !_game.Player.State.Flags.GetValueOrDefault(quest.Flag);
    }

    /// <summary>Dipanggil SpawnSystem saat mob hilang: catat kill + hadiah XP.</summary>
    public void RegisterKill(Mob mob)
    {
        var game = _game;
        if (game.Player is null) return;
        game.Player.State.Defeated[mob.Stats.Id] = game.Player.State.Defeated.GetValueOrDefault(mob.Stats.Id) + 1;
        game.Player.State.Kills++;
        game.Player.AddXp(mob.Stats.Xp);
        game.Hud?.Toast($"{mob.Stats.Display} tumbang  ·  +{mob.Stats.Xp} XP");
        if (mob.Stats.Boss)
        {
            game.Hud?.Toast($"{mob.Stats.Display} dikalahkan!");
            game.LogEvent("boss_defeated", mob.Stats.Display);
            game.Autosave("boss kalah");
        }
        game.Bus.RaiseMobKilled(mob);
    }

    /// <summary>Naik level (dari Player.LeveledUp): efek + catatan journey.</summary>
    public void OnLevelUp(int level)
    {
        var game = _game;
        game.Sfx?.Play("levelup");
        game.Hud?.Toast($"Naik level {level}! HP penuh, skill Aura Slash siap.");
        game.LogEvent("level_up", $"level {level}");
        game.Bus.RaiseLevelUp(level);
    }

    /// <summary>Tandai quest selesai: flag, hadiah XP, catatan, simpan.</summary>
    public void CompleteQuest(NpcQuestSpec quest, NpcInfo giver)
    {
        var game = _game;
        if (game.Player is null) return;
        game.Player.State.Flags[quest.Flag] = true;
        game.Sfx?.Play("quest");
        game.Player.AddXp(quest.RewardXp);
        game.Hud?.Toast($"Quest selesai: {giver.Name} — +{quest.RewardXp} XP");
        game.LogEvent("quest", quest.Flag);
        game.Autosave("quest selesai");
        game.Bus.RaiseQuestFlagSet(quest.Flag);
    }
}
