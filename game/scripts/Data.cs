using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RpgGame;

public sealed class TileObjectInfo
{
    public string Name = "";
    public int W = 1, H = 1, Col, Row, X, Y, SortY;
    public bool Above;
    public string Kind = "";
    public List<(int dx, int dy, bool solid)> Cells = new();
}

public sealed class TilesetData
{
    public int TileSize = 16;
    public int Columns, Rows;
    public readonly Dictionary<string, TileObjectInfo> Objects = new();
    public readonly Dictionary<string, (int col, int row, bool solid)> Cells = new();
    public readonly Dictionary<string, (int col, int row)> TileCoords = new();

    public static TilesetData Load(string path)
    {
        using var doc = Assets.LoadJson(path);
        var root = doc.RootElement;
        var data = new TilesetData
        {
            TileSize = root.GetProperty("tile_size").GetInt32(),
            Columns = root.GetProperty("columns").GetInt32(),
            Rows = root.GetProperty("rows").GetInt32(),
        };

        foreach (var prop in root.GetProperty("tiles").EnumerateObject())
        {
            var t = prop.Value;
            var info = new TileObjectInfo
            {
                Name = prop.Name,
                W = t.GetProperty("w").GetInt32(),
                H = t.GetProperty("h").GetInt32(),
                Col = t.GetProperty("col").GetInt32(),
                Row = t.GetProperty("row").GetInt32(),
                X = t.GetProperty("x").GetInt32(),
                Y = t.GetProperty("y").GetInt32(),
                Above = t.GetProperty("above").GetBoolean(),
                SortY = t.GetProperty("sort_y").GetInt32(),
                Kind = t.GetProperty("kind").GetString() ?? "",
            };
            foreach (var cell in t.GetProperty("solid_cells").EnumerateArray())
            {
                var pair = cell.EnumerateArray().ToArray();
                info.Cells.Add((pair[0].GetInt32(), pair[1].GetInt32(), true));
            }
            data.Objects[prop.Name] = info;
            data.TileCoords[prop.Name] = (info.Col, info.Row);
        }

        foreach (var cell in root.GetProperty("cells").EnumerateArray())
        {
            var name = cell.GetProperty("name").GetString() ?? "";
            data.Cells[name] = (cell.GetProperty("col").GetInt32(), cell.GetProperty("row").GetInt32(),
                cell.GetProperty("solid").GetBoolean());
        }
        return data;
    }
}

public sealed class PropPlacement
{
    public string Group = "";
    public int Col, Row;
}

public sealed class ZoneInfo
{
    public string Id = "", Name = "";
    public int X, Y, W, H;
}

public sealed class CheckpointInfo
{
    public string Id = "", Name = "", Zone = "";
    public int Col, Row;
}

public sealed class EnemySpawnInfo
{
    public string Type = "", Zone = "";
    public int Col, Row;
}

public sealed class ChestInfo
{
    public string Id = "", Zone = "";
    public int Col, Row, Gold;
}

/// <summary>Bekas terbakar (dekoratif): di dalamnya ada abu beterbangan + tunas tumbuh.</summary>
public sealed class BurntZoneInfo
{
    public string Id = "", Name = "";
    public int X, Y, W, H;

    /// <summary>Batas zona dalam piksel dunia (untuk culling partikel & cek kamera).</summary>
    public Rect2 RectPx(int tile) => new(X * tile, Y * tile, W * tile, H * tile);
}

public sealed class NpcQuestSpec
{
    public string Type = "kill";
    public string Target = "slime";
    public string Flag = "";
    public string Intro = "";
    public string DoneLine = "";
    public int Count = 1;
    public int RewardXp;
}

public sealed class NpcInfo
{
    public string Id = "", Name = "", Role = "", Facing = "down", Zone = "";
    public int Col, Row;
    public readonly List<string> Lines = new();
    public NpcQuestSpec? Quest;
}

public sealed class WorldData
{
    public int TileSize = 16, Width, Height;
    public List<string> TileNames = new();
    public int[][] Ground = Array.Empty<int[]>();
    public List<PropPlacement> Props = new();
    public bool[][] Walkable = Array.Empty<bool[]>();
    public Vector2I Spawn;
    public string SpawnZone = "";
    public readonly List<ZoneInfo> Zones = new();
    public readonly List<CheckpointInfo> Checkpoints = new();
    public readonly List<EnemySpawnInfo> Enemies = new();
    public readonly List<NpcInfo> Npcs = new();
    public readonly List<ChestInfo> Chests = new();
    public readonly List<BurntZoneInfo> BurntZones = new();

    public Vector2 PixelSize => new(Width * TileSize, Height * TileSize);

    public static WorldData Load(string path)
    {
        using var doc = Assets.LoadJson(path);
        var root = doc.RootElement;
        var data = new WorldData
        {
            TileSize = root.GetProperty("tile_size").GetInt32(),
            Width = root.GetProperty("width").GetInt32(),
            Height = root.GetProperty("height").GetInt32(),
        };

        foreach (var name in root.GetProperty("tile_names").EnumerateArray())
            data.TileNames.Add(name.GetString() ?? "");

        data.Ground = root.GetProperty("ground_tiles").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(v => v.GetInt32()).ToArray())
            .ToArray();

        data.Walkable = root.GetProperty("walkable").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(v => v.GetInt32() != 0).ToArray())
            .ToArray();

        foreach (var prop in root.GetProperty("props").EnumerateArray())
        {
            data.Props.Add(new PropPlacement
            {
                Group = prop.GetProperty("group").GetString() ?? "",
                Col = prop.GetProperty("col").GetInt32(),
                Row = prop.GetProperty("row").GetInt32(),
            });
        }

        var markers = root.GetProperty("markers");
        var spawn = markers.GetProperty("spawn");
        data.Spawn = new Vector2I(spawn.GetProperty("col").GetInt32(), spawn.GetProperty("row").GetInt32());
        data.SpawnZone = spawn.TryGetProperty("zone", out var sz) ? sz.GetString() ?? "" : "";

        foreach (var zone in markers.GetProperty("zones").EnumerateArray())
        {
            var rect = zone.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            data.Zones.Add(new ZoneInfo
            {
                Id = zone.GetProperty("id").GetString() ?? "",
                Name = zone.GetProperty("name").GetString() ?? "",
                X = rect[0], Y = rect[1], W = rect[2], H = rect[3],
            });
        }

        foreach (var cp in markers.GetProperty("checkpoints").EnumerateArray())
        {
            data.Checkpoints.Add(new CheckpointInfo
            {
                Id = cp.GetProperty("id").GetString() ?? "",
                Name = cp.GetProperty("name").GetString() ?? "",
                Zone = cp.TryGetProperty("zone", out var z) ? z.GetString() ?? "" : "",
                Col = cp.GetProperty("col").GetInt32(),
                Row = cp.GetProperty("row").GetInt32(),
            });
        }

        if (markers.TryGetProperty("burnt", out var burntArray) && burntArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var zone in burntArray.EnumerateArray())
            {
                var rect = zone.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                data.BurntZones.Add(new BurntZoneInfo
                {
                    Id = zone.GetProperty("id").GetString() ?? "",
                    Name = zone.GetProperty("name").GetString() ?? "",
                    X = rect[0], Y = rect[1], W = rect[2], H = rect[3],
                });
            }
        }

        foreach (var mob in markers.GetProperty("enemies").EnumerateArray())
        {
            data.Enemies.Add(new EnemySpawnInfo
            {
                Type = mob.GetProperty("type").GetString() ?? "",
                Zone = mob.GetProperty("zone").GetString() ?? "",
                Col = mob.GetProperty("col").GetInt32(),
                Row = mob.GetProperty("row").GetInt32(),
            });
        }

        if (markers.TryGetProperty("npcs", out var npcArray) && npcArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var npc in npcArray.EnumerateArray())
            {
                var info = new NpcInfo
                {
                    Id = npc.GetProperty("id").GetString() ?? "",
                    Name = npc.GetProperty("name").GetString() ?? "",
                    Role = npc.TryGetProperty("role", out var role) ? role.GetString() ?? "" : "",
                    Facing = npc.TryGetProperty("facing", out var facing) ? facing.GetString() ?? "down" : "down",
                    Zone = npc.TryGetProperty("zone", out var zone) ? zone.GetString() ?? "" : "",
                    Col = npc.GetProperty("col").GetInt32(),
                    Row = npc.GetProperty("row").GetInt32(),
                };
                foreach (var line in npc.GetProperty("lines").EnumerateArray())
                    info.Lines.Add(line.GetString() ?? "");
                if (npc.TryGetProperty("quest", out var quest) && quest.ValueKind == JsonValueKind.Object)
                {
                    info.Quest = new NpcQuestSpec
                    {
                        Type = quest.TryGetProperty("type", out var type) ? type.GetString() ?? "kill" : "kill",
                        Target = quest.TryGetProperty("target", out var target) ? target.GetString() ?? "slime" : "slime",
                        Flag = quest.TryGetProperty("flag", out var flag) ? flag.GetString() ?? "" : "",
                        Intro = quest.TryGetProperty("intro", out var intro) ? intro.GetString() ?? "" : "",
                        DoneLine = quest.TryGetProperty("done_line", out var done) ? done.GetString() ?? "" : "",
                        Count = quest.TryGetProperty("count", out var count) ? count.GetInt32() : 1,
                        RewardXp = quest.TryGetProperty("reward_xp", out var reward) ? reward.GetInt32() : 0,
                    };
                }
                data.Npcs.Add(info);
            }
        }

        if (markers.TryGetProperty("chests", out var chestArray) && chestArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var chest in chestArray.EnumerateArray())
            {
                data.Chests.Add(new ChestInfo
                {
                    Id = chest.GetProperty("id").GetString() ?? "",
                    Col = chest.GetProperty("col").GetInt32(),
                    Row = chest.GetProperty("row").GetInt32(),
                    Gold = chest.TryGetProperty("gold", out var gold) ? gold.GetInt32() : 0,
                    Zone = chest.TryGetProperty("zone", out var zone) ? zone.GetString() ?? "" : "",
                });
            }
        }
        return data;
    }

    /// <summary>Zona pertama yang memuat tile tsb (urutan di JSON = paling spesifik dulu).</summary>
    public ZoneInfo? ZoneAt(int col, int row)
    {
        foreach (var zone in Zones)
        {
            if (col >= zone.X && col < zone.X + zone.W && row >= zone.Y && row < zone.Y + zone.H)
                return zone;
        }
        return null;
    }
}

public sealed class SpriteFrameInfo
{
    public int X, Y, W, H;
}

public sealed class SpriteAnimationInfo
{
    public List<string> Frames = new();
    public double Fps = 8;
    public bool Loop = true;
    public bool Mirror;
}

public sealed class HeroData
{
    public int CellW = 64, CellH = 40;
    public Vector2I Foot;
    public readonly Dictionary<string, SpriteFrameInfo> Frames = new();
    public readonly Dictionary<string, SpriteAnimationInfo> Animations = new();

    public static HeroData Load(string path)
    {
        using var doc = Assets.LoadJson(path);
        var root = doc.RootElement;
        var data = new HeroData
        {
            CellW = root.GetProperty("cell").GetProperty("w").GetInt32(),
            CellH = root.GetProperty("cell").GetProperty("h").GetInt32(),
            Foot = new Vector2I(root.GetProperty("foot").GetProperty("x").GetInt32(),
                root.GetProperty("foot").GetProperty("y").GetInt32()),
        };
        foreach (var frame in root.GetProperty("frames").EnumerateObject())
        {
            var f = frame.Value;
            data.Frames[frame.Name] = new SpriteFrameInfo
            {
                X = f.GetProperty("x").GetInt32(), Y = f.GetProperty("y").GetInt32(),
                W = f.GetProperty("w").GetInt32(), H = f.GetProperty("h").GetInt32(),
            };
        }
        foreach (var anim in root.GetProperty("animations").EnumerateObject())
        {
            var a = anim.Value;
            var info = new SpriteAnimationInfo
            {
                Fps = a.TryGetProperty("fps", out var fps) ? fps.GetDouble() : 8,
                Loop = a.TryGetProperty("loop", out var loop) && loop.GetBoolean(),
                Mirror = a.TryGetProperty("mirror", out var m) && m.GetBoolean(),
            };
            foreach (var frameName in a.GetProperty("frames").EnumerateArray())
                info.Frames.Add(frameName.GetString() ?? "");
            data.Animations[anim.Name] = info;
        }
        return data;
    }
}

public sealed class EnemyData
{
    public int CellW = 40, CellH = 40;
    public Vector2I Foot;
    public readonly Dictionary<string, SpriteFrameInfo> Frames = new();
    public readonly Dictionary<string, Dictionary<string, SpriteAnimationInfo>> PerEnemy = new();

    public static EnemyData Load(string path)
    {
        using var doc = Assets.LoadJson(path);
        var root = doc.RootElement;
        var data = new EnemyData
        {
            CellW = root.GetProperty("cell").GetProperty("w").GetInt32(),
            CellH = root.GetProperty("cell").GetProperty("h").GetInt32(),
            Foot = new Vector2I(root.GetProperty("foot").GetProperty("x").GetInt32(),
                root.GetProperty("foot").GetProperty("y").GetInt32()),
        };
        foreach (var frame in root.GetProperty("frames").EnumerateObject())
        {
            var f = frame.Value;
            data.Frames[frame.Name] = new SpriteFrameInfo
            {
                X = f.GetProperty("x").GetInt32(), Y = f.GetProperty("y").GetInt32(),
                W = f.GetProperty("w").GetInt32(), H = f.GetProperty("h").GetInt32(),
            };
        }
        foreach (var enemy in root.GetProperty("enemies").EnumerateObject())
        {
            var anims = new Dictionary<string, SpriteAnimationInfo>();
            foreach (var anim in enemy.Value.GetProperty("animations").EnumerateObject())
            {
                var info = new SpriteAnimationInfo { Fps = 6, Loop = true };
                foreach (var frameName in anim.Value.EnumerateArray())
                    info.Frames.Add(frameName.GetString() ?? "");
                if (anim.Name is "attack" or "hurt" or "death")
                {
                    info.Loop = false;
                    info.Fps = anim.Name == "attack" ? 8 : 6;
                }
                anims[anim.Name] = info;
            }
            data.PerEnemy[enemy.Name] = anims;
        }
        return data;
    }
}

/// <summary>Atlas NPC desa (tools/build_npc_sprites.py): idle 2 frame per arah.</summary>
public sealed class NpcData
{
    public int CellW = 64, CellH = 40;
    public Vector2I Foot;
    public readonly Dictionary<string, SpriteFrameInfo> Frames = new();
    public readonly Dictionary<string, Dictionary<string, SpriteAnimationInfo>> PerNpc = new();

    public static NpcData Load(string path)
    {
        using var doc = Assets.LoadJson(path);
        var root = doc.RootElement;
        var data = new NpcData
        {
            CellW = root.GetProperty("cell").GetProperty("w").GetInt32(),
            CellH = root.GetProperty("cell").GetProperty("h").GetInt32(),
            Foot = new Vector2I(root.GetProperty("foot").GetProperty("x").GetInt32(),
                root.GetProperty("foot").GetProperty("y").GetInt32()),
        };
        foreach (var frame in root.GetProperty("frames").EnumerateObject())
        {
            var f = frame.Value;
            data.Frames[frame.Name] = new SpriteFrameInfo
            {
                X = f.GetProperty("x").GetInt32(), Y = f.GetProperty("y").GetInt32(),
                W = f.GetProperty("w").GetInt32(), H = f.GetProperty("h").GetInt32(),
            };
        }
        foreach (var npc in root.GetProperty("animations").EnumerateObject())
        {
            var anims = new Dictionary<string, SpriteAnimationInfo>();
            foreach (var anim in npc.Value.EnumerateObject())
            {
                var info = new SpriteAnimationInfo { Fps = 4, Loop = true };
                foreach (var frameName in anim.Value.EnumerateArray())
                    info.Frames.Add(frameName.GetString() ?? "");
                anims[anim.Name] = info;
            }
            data.PerNpc[npc.Name] = anims;
        }
        return data;
    }
}
