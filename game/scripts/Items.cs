using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RpgGame;

public sealed class ItemInfo
{
    public string Id = "", Name = "", Kind = "", Description = "";
}

public sealed class RecipeInfo
{
    public string Id = "", Name = "", Result = "";
    public int Count = 1;
    public readonly Dictionary<string, int> Cost = new();
}

/// <summary>
/// Katalog barang & resep data-driven (assets/data/items.json). Dimuat sekali;
/// dipakai InventorySystem, HUD crafting, peti, dan tes.
/// </summary>
public static class ItemCatalog
{
    public const string Path = "res://assets/data/items.json";

    private static readonly List<ItemInfo> _items = new();
    private static readonly List<RecipeInfo> _recipes = new();
    private static bool _loaded;

    public static IReadOnlyList<ItemInfo> Items => _items;
    public static IReadOnlyList<RecipeInfo> Recipes => _recipes;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        using var doc = Assets.LoadJson(Path);
        var root = doc.RootElement;
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            _items.Add(new ItemInfo
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Name = item.GetProperty("name").GetString() ?? "",
                Kind = item.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "" : "",
                Description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
            });
        }
        foreach (var recipe in root.GetProperty("recipes").EnumerateArray())
        {
            var info = new RecipeInfo
            {
                Id = recipe.GetProperty("id").GetString() ?? "",
                Name = recipe.GetProperty("name").GetString() ?? "",
                Result = recipe.GetProperty("result").GetString() ?? "",
                Count = recipe.TryGetProperty("count", out var count) ? count.GetInt32() : 1,
            };
            foreach (var cost in recipe.GetProperty("cost").EnumerateObject())
                info.Cost[cost.Name] = cost.Value.GetInt32();
            _recipes.Add(info);
        }
    }

    public static ItemInfo? Get(string id) => _items.FirstOrDefault(i => i.Id == id);

    public static RecipeInfo? Recipe(string id) => _recipes.FirstOrDefault(r => r.Id == id);

    public static string NameOf(string id)
    {
        EnsureLoaded();
        return Get(id)?.Name ?? id;
    }

    public static string CostText(RecipeInfo recipe) =>
        string.Join(" · ", recipe.Cost.Select(pair => $"{NameOf(pair.Key)} x{pair.Value}"));
}
