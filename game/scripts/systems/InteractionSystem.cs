using System;
using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>
/// Interaksi tombol E: bicara NPC, buka peti, minum/beli potion, istirahat di
/// checkpoint. Semua efek state (gold, potion, flag peti) di sini; efek visual
/// memakai Feel/Sfx/Hud lewat Game.
/// </summary>
public sealed class InteractionSystem
{
    private readonly Game _game;

    public InteractionSystem(Game game) => _game = game;

    /// <summary>Aksi tombol E: bicara dengan NPC terdekat; kalau tidak ada, istirahat di checkpoint.</summary>
    public bool Interact()
    {
        var game = _game;
        if (game.World is null || game.Player is null || !game.Player.Alive) return false;
        var info = game.World.NpcNear(game.Player.Position);
        if (info is not null)
        {
            game.OpenDialog(info);
            return true;
        }
        var chest = ChestNear(game.Player.Position);
        if (chest is not null)
        {
            if (ChestOpened(chest)) game.Hud?.Toast($"Peti {chest.Id} sudah kosong.");
            else OpenChest(chest);
            return true;
        }
        return RestAtCheckpoint();
    }

    public ChestInfo? ChestNear(Vector2 position)
    {
        var game = _game;
        if (game.World is null) return null;
        ChestInfo? best = null;
        var bestDistance = Rules.InteractRange;
        foreach (var chest in game.World.Data.Chests)
        {
            var pos = new Vector2((chest.Col + 0.5f) * World.TileSize, (chest.Row + 0.5f) * World.TileSize);
            var distance = pos.DistanceTo(position);
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = chest;
        }
        return best;
    }

    public bool ChestOpened(ChestInfo chest) =>
        _game.Player?.State.Flags.GetValueOrDefault($"chest_{chest.Id}") ?? false;

    /// <summary>Buka peti: gold + 1 potion, flag tersimpan supaya tidak bisa dicuri dua kali.</summary>
    public bool OpenChest(ChestInfo chest)
    {
        var game = _game;
        if (game.Player is null || ChestOpened(chest)) return false;
        game.Player.State.Flags[$"chest_{chest.Id}"] = true;
        game.Player.State.Gold += chest.Gold;
        game.Player.State.Potions = Math.Min(Rules.MaxPotions, game.Player.State.Potions + 1);
        game.World?.SetPropTile(chest.Col, chest.Row, "chest_open");
        game.Sfx?.Play("quest");
        // percikan emas dari mulut peti (visual, tahap 2f)
        var chestPos = new Vector2((chest.Col + 0.5f) * World.TileSize, (chest.Row + 0.5f) * World.TileSize);
        game.Feel?.Burst(chestPos - new Vector2(0, 4), new Color(1f, 0.85f, 0.38f, 0.95f), 10);
        var loot = new List<string> { "kain", "tali", "arang" };
        if (chest.Id == "chest_market") loot.Add("benih");
        foreach (var item in loot) game.Inventory?.Add(item);
        var lootText = string.Join(", ", loot.ConvertAll(i => ItemCatalog.NameOf(i)));
        game.Hud?.Toast($"Peti terbuka: +{chest.Gold} gold, +1 potion, {lootText}");
        game.LogEvent("chest", chest.Id);
        game.Autosave("peti terbuka");
        return true;
    }

    /// <summary>Minum potion (tombol Q).</summary>
    public bool DrinkPotion()
    {
        var game = _game;
        if (game.Player is null || !game.Player.Alive) return false;
        if (game.Player.State.Potions <= 0)
        {
            game.Hud?.Toast("Tidak ada potion. Beli di menu (Esc) kalau gold cukup.");
            return false;
        }
        if (game.Player.State.Hp >= game.Player.State.MaxHp)
        {
            game.Hud?.Toast("HP masih penuh — potion disimpan.");
            return false;
        }
        game.Player.State.Potions--;
        game.Player.State.Hp = Math.Min(game.Player.State.MaxHp, game.Player.State.Hp + Rules.PotionHeal);
        game.Sfx?.Play("rest");
        game.Hud?.Toast($"Potion: +{Rules.PotionHeal} HP · sisa {game.Player.State.Potions}");
        return true;
    }

    /// <summary>Beli potion di pasar (tombol di menu pause).</summary>
    public bool BuyPotion()
    {
        var game = _game;
        if (game.Player is null) return false;
        if (game.Player.State.Potions >= Rules.MaxPotions)
        {
            game.Hud?.Toast($"Persediaan potion penuh ({Rules.MaxPotions}).");
            return false;
        }
        if (game.Player.State.Gold < Rules.PotionPrice)
        {
            game.Hud?.Toast($"Gold tidak cukup — butuh {Rules.PotionPrice}, punya {game.Player.State.Gold}.");
            return false;
        }
        game.Player.State.Gold -= Rules.PotionPrice;
        game.Player.State.Potions++;
        game.Sfx?.Play("quest");
        game.Hud?.Toast($"Beli potion: -{Rules.PotionPrice} gold · sisa {game.Player.State.Gold}");
        game.Autosave("beli potion");
        return true;
    }

    /// <summary>Tombol E di checkpoint: istirahat memulihkan HP & energi, lalu simpan.</summary>
    public bool RestAtCheckpoint()
    {
        var game = _game;
        if (game.World is null || game.Player is null || !game.Player.Alive) return false;
        var checkpoint = game.World.CheckpointNear(game.Player.Position);
        if (checkpoint is null) return false;
        game.Player.State.Hp = game.Player.State.MaxHp;
        game.Player.Energy = Rules.MaxEnergy;
        game.Sfx?.Play("rest");
        game.Hud?.Toast($"Beristirahat di {checkpoint.Name} — HP & energi pulih");
        game.Autosave("istirahat");
        return true;
    }
}
