using Godot;

namespace RpgGame;

/// <summary>
/// Dunia & lokasi: melacak zona dan checkpoint pemain, memberi notifikasi +
/// autosave saat berpindah, serta memasang heat haze untuk FX api.
/// </summary>
public sealed class WorldManager
{
    private readonly Game _game;

    public WorldManager(Game game) => _game = game;

    public void UpdateZone(bool force)
    {
        var game = _game;
        if (game.World is null || game.Player is null) return;
        var zone = game.World.ZoneAt(game.Player.Position);
        var id = zone?.Id ?? game.ZoneId;
        var name = zone?.Name ?? game.ZoneName;
        if (!force && id == game.ZoneId) return;
        var first = game.ZoneId.Length == 0;
        game.ZoneId = id;
        game.ZoneName = name;
        game.Player.State.Zone = id;
        game.Sfx?.PlayMusic(zone?.Music ?? (id == "greenwood_vale" ? "music_village" : "music_wilds"));
        if (!first)
        {
            game.Hud?.Toast($"Masuk zona: {name}");
            game.LogEvent("zone", name);
            game.Autosave("pindah zona");
        }
        game.Bus.RaiseZoneChanged(id, name);
    }

    public void UpdateCheckpoint()
    {
        var game = _game;
        if (game.Player is null || game.World is null) return;
        var checkpoint = game.World.CheckpointNear(game.Player.Position);
        if (checkpoint is null || checkpoint.Id == game.Player.State.Checkpoint) return;
        game.Player.State.Checkpoint = checkpoint.Id;
        game.Hud?.Toast($"Checkpoint: {checkpoint.Name} — perjalanan disimpan");
        game.LogEvent("checkpoint", checkpoint.Name);
        game.Autosave("checkpoint");
    }

    /// <summary>
    /// Pasang heat haze di semua titik api dunia. Quad-nya ditaruh di node
    /// terpisah yang tidak ikut y-sort supaya shimmer selalu di depan sprite.
    /// </summary>
    public void AttachFireFx()
    {
        var game = _game;
        if (game.World is null || game.Fx is null) return;
        var heatRoot = new Node2D { Name = "HeatHaze", YSortEnabled = false };
        game.World.Root.AddChild(heatRoot);
        foreach (var (pos, phase, size) in game.World.HeatSources)
            game.Fx.AttachHeat(heatRoot, pos, size, phase);
    }
}
