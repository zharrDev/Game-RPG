using Godot;

namespace RpgGame;

/// <summary>
/// Spawn & perawatan aktor dunia: musuh dan warga desa. Daftarnya sendiri
/// tetap hidup di Game (Mobs/Npcs) supaya tes headless dan sistem lain bisa
/// membacanya tanpa perantara; sistem ini yang mengisi dan men-tick mereka.
/// </summary>
public sealed class SpawnSystem
{
    private readonly Game _game;

    public SpawnSystem(Game game) => _game = game;

    public void SpawnMobs()
    {
        var game = _game;
        if (game.World is null) return;
        var id = 0;
        foreach (var spawn in game.World.Data.Enemies)
        {
            var mob = new Mob(game.World, spawn, id++);
            mob.Removed += OnMobRemoved;
            game.Mobs.Add(mob);
            game.World.Root.AddChild(mob);
        }
    }

    public void SpawnNpcs()
    {
        var game = _game;
        if (game.World is null) return;
        foreach (var info in game.World.Data.Npcs)
        {
            var npc = new Npc(game.World, info);
            game.Npcs.Add(npc);
            game.World.Root.AddChild(npc);
        }
        RefreshNpcMarkers();
    }

    public void RefreshNpcMarkers()
    {
        var game = _game;
        if (game.Player is null) return;
        foreach (var npc in game.Npcs) npc.SetQuestPending(game.QuestPending(npc.Info));
    }

    private void OnMobRemoved(Mob mob)
    {
        _game.Mobs.Remove(mob);
        _game.Progress.RegisterKill(mob);
    }

    /// <summary>Warga mondar-mandir singkat (tahap 2f, murni visual); berhenti saat dialog/menu/hit-stop.</summary>
    public void TickNpcs(double delta, bool paused)
    {
        foreach (var npc in _game.Npcs)
        {
            if (!GodotObject.IsInstanceValid(npc)) continue;
            npc.Tick(delta, paused);
        }
    }
}
