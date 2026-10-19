using System;
using System.Collections.Generic;
using System.Linq;

namespace RpgGame;

/// <summary>
/// Tas & crafting (M5): jumlah barang disimpan di GameState.Inventory supaya
/// ikut semua jalur save, resep dimuat dari assets/data/items.json.
/// </summary>
public sealed class InventorySystem
{
    private readonly Game _game;

    public InventorySystem(Game game)
    {
        _game = game;
        ItemCatalog.EnsureLoaded();
    }

    private Dictionary<string, int> Bag => _game.Player!.State.Inventory;

    public int Count(string id) => _game.Player is null ? 0 : Bag.GetValueOrDefault(id);

    public bool Has(string id, int count = 1) => Count(id) >= count;

    public bool Add(string id, int count = 1)
    {
        if (_game.Player is null || count <= 0 || ItemCatalog.Get(id) is null) return false;
        Bag[id] = Math.Min(99, Bag.GetValueOrDefault(id) + count);
        _game.Objectives?.Advance();
        _game.Hud?.RefreshCraft(Detailed());
        return true;
    }

    public bool Remove(string id, int count = 1)
    {
        if (count <= 0 || !Has(id, count)) return false;
        var left = Bag[id] - count;
        if (left <= 0) Bag.Remove(id);
        else Bag[id] = left;
        _game.Hud?.RefreshCraft(Detailed());
        return true;
    }

    public bool CanCraft(string recipeId)
    {
        var recipe = ItemCatalog.Recipe(recipeId);
        return recipe is not null && recipe.Cost.All(pair => Has(pair.Key, pair.Value));
    }

    public bool Craft(string recipeId)
    {
        var recipe = ItemCatalog.Recipe(recipeId);
        if (recipe is null) return false;
        if (!CanCraft(recipeId))
        {
            var missing = string.Join(", ", recipe.Cost.Where(p => !Has(p.Key, p.Value))
                .Select(p => $"{ItemCatalog.NameOf(p.Key)} x{p.Value - Count(p.Key)}"));
            _game.Hud?.Toast($"Bahan kurang: {missing}");
            return false;
        }
        foreach (var (item, count) in recipe.Cost) Remove(item, count);
        Bag[recipe.Result] = Math.Min(99, Bag.GetValueOrDefault(recipe.Result) + recipe.Count);
        _game.Player!.State.Flags[$"crafted_{recipe.Id}"] = true;
        _game.Sfx?.Play("quest");
        _game.Hud?.Toast($"Crafting: {recipe.Name} jadi. {Summary()}");
        _game.LogEvent("craft", recipe.Id);
        _game.Objectives?.NotifyCrafted(recipe.Id);
        _game.Hud?.RefreshCraft(Detailed());
        _game.Autosave("crafting");
        return true;
    }

    /// <summary>Ringkasan tas untuk HUD/pause: "Kain perca x2 · Tali serat x1".</summary>
    public string Summary()
    {
        if (_game.Player is null || Bag.Count == 0) return "(tas kosong)";
        return string.Join(" · ", Bag.Where(p => p.Value > 0).OrderBy(p => p.Key)
            .Select(p => $"{ItemCatalog.NameOf(p.Key)} x{p.Value}"));
    }

    /// <summary>Isi tas untuk HUD crafting (baris per barang).</summary>
    public string Detailed()
    {
        if (_game.Player is null || Bag.Count == 0) return "Tas kosong — buka peti (E) untuk bahan.";
        return string.Join("\n", Bag.Where(p => p.Value > 0).OrderBy(p => p.Key)
            .Select(p => $"{ItemCatalog.NameOf(p.Key)} × {p.Value}"));
    }
}
