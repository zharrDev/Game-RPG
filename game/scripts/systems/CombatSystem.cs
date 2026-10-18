using System;
using System.Linq;
using Godot;

namespace RpgGame;

/// <summary>
/// Tempur: serangan pemain, damage masuk dari musuh, respawn, dan tick AI
/// musuh. Efek visual umpan balik (shake/hit-stop/flash HUD) tidak dipasang di
/// sini — CombatSystem hanya mengirim event lewat EventBus, Game yang
/// menyambungkannya ke GameFeel/Hud (membuat sistem ini bisa dites tanpa HUD).
/// </summary>
public sealed class CombatSystem
{
    private readonly Game _game;

    public CombatSystem(Game game) => _game = game;

    /// <summary>Tick semua musuh (gameplay) — hanya kalau pemain hidup & bisa main.</summary>
    public void TickMobs(double delta, bool canAttack)
    {
        var game = _game;
        if (game.World is null || game.Player is null) return;
        foreach (var mob in game.Mobs.ToArray())
        {
            if (!GodotObject.IsInstanceValid(mob)) continue;
            mob.Tick(delta, game.Player.Center, game.Player.Alive && canAttack, game.World.Grid, OnMobHitsPlayer);
        }
    }

    /// <summary>Tick semua musuh (dipakai tes headless supaya timer kematian berjalan).</summary>
    public void MobsTickForTest(double delta)
    {
        var game = _game;
        if (game.World is null || game.Player is null) return;
        foreach (var mob in game.Mobs.ToArray())
        {
            if (!GodotObject.IsInstanceValid(mob)) continue;
            mob.Tick(delta, game.Player.Center, false, game.World.Grid, (_, _) => { });
        }
    }

    /// <summary>Selesaikan serangan pemain: cari musuh yang kena, kurangi HP.</summary>
    public void ApplyStrike(Vector2 origin, string facing, int damage, string kind)
    {
        var game = _game;
        if (game.World is null) return;
        var dir = facing switch
        {
            "left" => new Vector2(-1, 0),
            "right" => new Vector2(1, 0),
            "up" => new Vector2(0, -1),
            _ => new Vector2(0, 1),
        };
        var range = kind == "skill" ? Rules.SkillRange : Rules.AttackRange;
        game.Sfx?.Play(kind == "skill" ? "skill" : "slash");
        var hit = 0;
        Vector2? firstHit = null;
        foreach (var mob in game.Mobs.ToArray())
        {
            if (mob.Dead || !GodotObject.IsInstanceValid(mob)) continue;
            var offset = mob.Position - origin;
            var distance = offset.Length();
            if (distance > range) continue;
            if (kind != "skill" && distance > 0.5f && dir.Dot(offset.Normalized()) < Rules.AttackArcDot) continue;
            hit++;
            firstHit ??= mob.Position - new Vector2(0, 8);
            var died = mob.TakeDamage(damage);
            var label = kind == "skill" ? "Aura Slash" : "Serangan";
            game.Hud?.Toast($"{label}: {mob.Stats.Display} -{damage} HP" + (died ? " (tumbang)" : ""));
        }
        if (hit > 0)
        {
            game.Sfx?.Play("hit");
            game.Feel?.Shake(kind == "skill" ? 3.2f : 2.4f);
            game.Feel?.HitStop(0.055);
            if (firstHit is not null) game.Feel?.Burst(firstHit.Value, new Color(1f, 0.86f, 0.5f, 0.95f), 6);
        }
        if (hit == 0 && kind == "skill") game.Hud?.Toast("Aura Slash: tidak ada musuh di jangkauan");
    }

    private void OnMobHitsPlayer(Mob mob, int damage)
    {
        var game = _game;
        if (game.Player is null || !game.Player.Alive) return;
        var died = game.Player.TakeDamage(damage);
        if (died)
        {
            game.Player.State.Deaths++;
            game.LogEvent("death", $"dikalahkan {mob.Stats.Display} di {game.ZoneName}");
        }
        game.Bus.RaisePlayerDamaged(mob, damage, died);
    }

    public void Respawn()
    {
        var game = _game;
        if (game.Player is null || game.World is null) return;
        var checkpoint = game.World.Data.Checkpoints.FirstOrDefault(c => c.Id == game.Player.State.Checkpoint)
                         ?? game.World.Data.Checkpoints.FirstOrDefault();
        var pos = checkpoint is null
            ? new Vector2((game.World.Data.Spawn.X + 0.5f) * World.TileSize, (game.World.Data.Spawn.Y + 1f) * World.TileSize)
            : new Vector2((checkpoint.Col + 0.5f) * World.TileSize, (checkpoint.Row + 1f) * World.TileSize);
        game.Player.Position = pos;
        game.Player.Action = "idle";
        game.Player.DeathTimer = 0;
        game.Player.JumpTimer = 0;
        game.Player.State.Hp = Math.Max(1, game.Player.State.MaxHp / 2);
        game.CloseDialog();
        game.Player.SyncFromState();
        game.Hud?.Toast("Kau tumbang... bangkit di checkpoint terakhir.");
        game.Autosave("kebangkitan");
    }
}
