using System;
using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>
/// Tabrakan berbasis grid tile (hasil tools/generate_world_map.py). Dipakai
/// semua aktor supaya pergerakan deterministik dan bisa diuji headless tanpa
/// bergantung pada physics server.
/// </summary>
public sealed class CollisionGrid
{
    private readonly bool[][] _walkable;
    private readonly int _width, _height, _tile;

    public CollisionGrid(bool[][] walkable, int width, int height, int tile)
    {
        _walkable = walkable;
        _width = width;
        _height = height;
        _tile = tile;
    }

    public int Width => _width;
    public int Height => _height;
    public int TileSize => _tile;

    public bool Walkable(int col, int row) =>
        col >= 0 && row >= 0 && col < _width && row < _height && _walkable[row][col];

    public bool WalkableAt(Vector2 pixel) =>
        Walkable((int)Math.Floor(pixel.X / _tile), (int)Math.Floor(pixel.Y / _tile));

    /// <summary>
    /// Semua tile yang tersentuh tapak kaki harus walkable. Tapak = lebar halfW di
    /// kiri/kanan titik kaki, dan setinggi height di ATAS titik kaki (titik kaki = piksel
    /// paling bawah sprite), jadi pemain bisa berdiri tepat di tepi tile.
    /// </summary>
    public bool BoxFree(Vector2 feet, Vector2 half)
    {
        var minCol = (int)Math.Floor((feet.X - half.X) / _tile);
        var maxCol = (int)Math.Floor((feet.X + half.X) / _tile);
        var minRow = (int)Math.Floor((feet.Y - half.Y) / _tile);
        var maxRow = (int)Math.Floor(feet.Y / _tile);
        for (var row = minRow; row <= maxRow; row++)
            for (var col = minCol; col <= maxCol; col++)
                if (!Walkable(col, row))
                    return false;
        return true;
    }

    /// <summary>Gerak per sumbu: kalau sumbu itu tertabrak, sumbu itu dibatalkan saja.</summary>
    public Vector2 Move(Vector2 feet, Vector2 delta, Vector2 half)
    {
        var result = feet;
        if (delta.X != 0)
        {
            var tryPos = new Vector2(result.X + delta.X, result.Y);
            if (BoxFree(tryPos, half)) result.X = tryPos.X;
        }
        if (delta.Y != 0)
        {
            var tryPos = new Vector2(result.X, result.Y + delta.Y);
            if (BoxFree(tryPos, half)) result.Y = tryPos.Y;
        }
        return result;
    }

    public Vector2 Clamp(Vector2 feet, Vector2 half)
    {
        var maxX = _width * _tile - half.X - 1;
        var maxY = _height * _tile - 1;
        return new Vector2(Math.Clamp(feet.X, half.X + 1, maxX), Math.Clamp(feet.Y, half.Y + 1, maxY));
    }
}

/// <summary>Dunia hasil rakitan: layer tanah, prop, grid, dan frame karakter.</summary>
public sealed class World
{
    public required Node2D Root;
    public required TileMapLayer Ground;
    public required Node2D PropRoot;
    public required WorldData Data;
    public required TilesetData Tileset;
    public required CollisionGrid Grid;
    public required Texture2D TilesetTexture;
    public required Texture2D HeroTexture;
    public required Texture2D EnemyTexture;
    public required HeroData Hero;
    public required EnemyData Enemies;
    public required NpcData Npcs;
    public required Texture2D NpcTexture;
    public required SpriteFrames HeroFrames;
    public readonly Dictionary<string, SpriteFrames> EnemyFrames = new();
    public readonly Dictionary<string, SpriteFrames> NpcFrames = new();
    /// <summary>Obor/lentera yang sinarnya berkedip (dipakai Game._Process).</summary>
    public readonly List<(Sprite2D Sprite, float Phase)> FlickerProps = new();
    /// <summary>Titik api nyata (obor, lentera, api unggun) untuk heat haze + PointLight2D.</summary>
    public readonly List<(Vector2 Pos, float Phase, float Size)> HeatSources = new();
    public Texture2D ShadowTexture = null!;
    public int GroundCellCount;
    public int GroundSourceId;

    /// <summary>Sel air + varian tile-nya (untuk animasi riak dua/tiga fase).</summary>
    private readonly Dictionary<Vector2I, string[]> _waterCells = new();
    public IReadOnlyDictionary<Vector2I, string[]> WaterCells => _waterCells;
    public int WaterPhase { get; private set; }

    /// <summary>Offset fase riak per sel: air berganti tidak serentak (riak menjalar).</summary>
    private readonly Dictionary<Vector2I, int> _waterOffset = new();

    /// <summary>Varian yang sedang tampil di sel air (untuk tes determinisme riak).</summary>
    public int WaterVariantIndex(Vector2I cell) =>
        _waterCells.TryGetValue(cell, out var variants)
            ? (WaterPhase + _waterOffset[cell]) % variants.Length
            : 0;

    /// <summary>Overlay tanah hangus (dekoratif) di atas ground; null di mode sprite.</summary>
    public TileMapLayer? Scorched;
    public int ScorchedCellCount;

    /// <summary>Peta mini 1 px per tile yang dibangun dari data ground.</summary>
    public Texture2D Minimap = null!;
    public Vector2I MinimapSize;

    /// <summary>Sprite prop per tile kiri-atas (dipakai untuk menukar peti tertutup -> terbuka).</summary>
    private readonly Dictionary<(int col, int row), Sprite2D> _propSprites = new();
    public Sprite2D? PropSpriteAt(int col, int row) =>
        _propSprites.TryGetValue((col, row), out var sprite) ? sprite : null;

    public const int TileSize = 16;

    public static World Build(WorldData data, TilesetData tileset, bool useTilemapGround = true)
    {
        var root = new Node2D { Name = "World", YSortEnabled = true };
        var texture = Assets.LoadTexture(Assets.TilesetPng);
        var heroTexture = Assets.LoadTexture(Assets.HeroPng);
        var enemyTexture = Assets.LoadTexture(Assets.EnemyPng);
        var npcTexture = Assets.LoadTexture(Assets.NpcPng);
        var ambientShadowTexture = Assets.MakeShadow(48, 18);
        var shadow = Assets.MakeShadow();

        var ground = new TileMapLayer
        {
            Name = "Ground",
            ZIndex = -20,
            YSortEnabled = false,
            CollisionEnabled = false,
        };
        var propRoot = new Node2D { Name = "Props", YSortEnabled = false };

        var world = new World
        {
            Root = root,
            Ground = ground,
            PropRoot = propRoot,
            Data = data,
            Tileset = tileset,
            Grid = new CollisionGrid(data.Walkable, data.Width, data.Height, data.TileSize),
            TilesetTexture = texture,
            HeroTexture = heroTexture,
            EnemyTexture = enemyTexture,
            Hero = HeroData.Load(Assets.HeroJson),
            Enemies = EnemyData.Load(Assets.EnemyJson),
            Npcs = NpcData.Load(Assets.NpcJson),
            NpcTexture = npcTexture,
            HeroFrames = new SpriteFrames(),
            ShadowTexture = shadow,
        };
        world.HeroFrames = world.BuildHeroFrames();

        if (useTilemapGround)
        {
            var tileSet = new TileSet { TileSize = new Vector2I(TileSize, TileSize) };
            var source = new TileSetAtlasSource
            {
                Texture = texture,
                TextureRegionSize = new Vector2I(TileSize, TileSize),
            };
            // buat tile untuk semua cell 1x1 di atlas (tile tanah semuanya 1x1)
            var created = new HashSet<Vector2I>();
            foreach (var (name, coords) in tileset.TileCoords)
            {
                var info = tileset.Objects[name];
                if (info.W != 1 || info.H != 1) continue;
                var atlasCoords = new Vector2I(coords.col, coords.row);
                if (created.Add(atlasCoords)) source.CreateTile(atlasCoords);
            }
            var sourceId = tileSet.AddSource(source);
            ground.TileSet = tileSet;
            world.GroundSourceId = sourceId;
            var used = 0;
            for (var row = 0; row < data.Height; row++)
            {
                for (var col = 0; col < data.Width; col++)
                {
                    var tileName = data.TileNames[data.Ground[row][col]];
                    if (!tileset.TileCoords.TryGetValue(tileName, out var coords)) continue;
                    ground.SetCell(new Vector2I(col, row), sourceId, new Vector2I(coords.col, coords.row));
                    var variants = WaterVariants(tileName, tileset);
                    if (variants is not null)
                    {
                        var cell = new Vector2I(col, row);
                        var offset = (int)(Hash01(col, row, 91) * 8f);
                        world._waterCells[cell] = variants;
                        world._waterOffset[cell] = offset;
                        // langsung tampilkan varian per-sel: riak tidak seragam sejak awal
                        var name = variants[offset % variants.Length];
                        if (tileset.TileCoords.TryGetValue(name, out var ripple))
                            ground.SetCell(cell, sourceId, new Vector2I(ripple.col, ripple.row));
                    }
                    used++;
                }
            }
            world.GroundCellCount = used;
            root.AddChild(ground);

            // tanah hangus dekoratif: overlay di atas ground, di bawah prop.
            // Hanya menghanguskan grass/meadow (jalan, air, bebatuan tetap bersih)
            // dan tepinya dibuat ragged agar tidak terlihat seperti kotak.
            var scorched = new TileMapLayer
            {
                Name = "Scorched",
                ZIndex = -15,
                YSortEnabled = false,
                CollisionEnabled = false,
                TileSet = tileSet,
            };
            foreach (var zone in data.BurntZones)
            {
                for (var row = zone.Y; row < zone.Y + zone.H; row++)
                {
                    for (var col = zone.X; col < zone.X + zone.W; col++)
                    {
                        if (row < 0 || row >= data.Height || col < 0 || col >= data.Width) continue;
                        var baseName = data.TileNames[data.Ground[row][col]];
                        if (!baseName.StartsWith("grass", StringComparison.Ordinal) &&
                            !baseName.StartsWith("meadow", StringComparison.Ordinal)) continue;
                        var edge = col == zone.X || col == zone.X + zone.W - 1 ||
                                   row == zone.Y || row == zone.Y + zone.H - 1;
                        if (edge && Hash01(col, row, 77) < 0.45f) continue;
                        var variant = 1 + (int)(Hash01(col, row, 78) * 3f) % 3;
                        if (!tileset.TileCoords.TryGetValue($"scorched_{variant}", out var scorchCoords))
                            continue;
                        scorched.SetCell(new Vector2I(col, row), sourceId,
                            new Vector2I(scorchCoords.col, scorchCoords.row));
                        world.ScorchedCellCount++;
                    }
                }
            }
            world.Scorched = scorched;
            root.AddChild(scorched);
        }
        else
        {
            // fallback: satu sprite latar (identik dengan preview hasil generator)
            var sprite = new Sprite2D
            {
                Name = "GroundSprite",
                Texture = Assets.LoadTexture("res://assets/world/overworld_ground.png"),
                Centered = false,
                Position = Vector2.Zero,
                ZIndex = -20,
            };
            root.AddChild(sprite);
        }

        foreach (var prop in data.Props)
        {
            if (!tileset.Objects.TryGetValue(prop.Group, out var info)) continue;
            var atlas = new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(info.X, info.Y, info.W * TileSize, info.H * TileSize),
            };
            var sprite = new Sprite2D
            {
                Name = prop.Group,
                Texture = atlas,
                Centered = false,
                Position = new Vector2((prop.Col + info.W / 2f) * TileSize, (prop.Row + info.H) * TileSize),
                Offset = new Vector2(-info.W * TileSize / 2f, -info.H * TileSize),
            };
            propRoot.AddChild(sprite);
            world._propSprites[(prop.Col, prop.Row)] = sprite;

            // bayangan ambient di bawah pohon/bangunan (lighting dari kiri-atas)
            if (info.Kind is "tree" or "prop" or "roof" or "wall")
            {
                var width = info.W * (float)TileSize;
                sprite.AddChild(new Sprite2D
                {
                    Name = "AmbientShadow",
                    Texture = ambientShadowTexture,
                    Centered = true,
                    ShowBehindParent = true,
                    Position = new Vector2(0, -2),
                    Scale = new Vector2(Math.Max(0.3f, width / 48f), Math.Clamp(width / 96f, 0.35f, 1f)),
                    Modulate = new Color(1, 1, 1, 0.3f),
                });
            }
            // obor & lentera berkedip hangat + catat titik api untuk heat haze
            if (prop.Group is "torch" or "lantern" or "campfire_1")
            {
                var phase = (prop.Col * 1.7f + prop.Row * 0.9f) % 6.283f;
                world.FlickerProps.Add((sprite, phase));
                var basePos = new Vector2((prop.Col + info.W / 2f) * TileSize, (prop.Row + info.H) * TileSize);
                world.HeatSources.Add((new Vector2(basePos.X, basePos.Y - 9f), phase,
                    prop.Group == "campfire_1" ? 52f : 44f));
            }
        }
        root.AddChild(propRoot);

        world.BuildEnemyFrames();
        world.BuildNpcFrames();
        world.Minimap = BuildMinimap(data);
        world.MinimapSize = new Vector2I(data.Width, data.Height);
        return world;
    }

    /// <summary>Nama-nama tile yang bisa dipakai bergantian untuk sel air ini.</summary>
    private static string[]? WaterVariants(string name, TilesetData tileset)
    {
        if (name.StartsWith("water_deep", StringComparison.Ordinal))
            return new[] { "water_deep_0", "water_deep_1", "water_deep_2" };
        if (!name.StartsWith("water_", StringComparison.Ordinal)) return null;
        var alternate = name + "_b";
        return tileset.TileCoords.ContainsKey(alternate) ? new[] { name, alternate } : null;
    }

    /// <summary>Ganti tampilan prop 1x1 (mis. chest_closed -> chest_open).</summary>
    public bool SetPropTile(int col, int row, string tileName)
    {
        if (!_propSprites.TryGetValue((col, row), out var sprite)) return false;
        if (!Tileset.Objects.TryGetValue(tileName, out var info)) return false;
        sprite.Texture = new AtlasTexture
        {
            Atlas = TilesetTexture,
            Region = new Rect2(info.X, info.Y, info.W * TileSize, info.H * TileSize),
        };
        return true;
    }

    /// <summary>Ganti fase riak air (dipanggil Game tiap ~0.6 detik).
    /// Offset per sel membuat riak menjalar dari satu sel ke sel lain.</summary>
    public void SetWaterPhase(int phase)
    {
        if (_waterCells.Count == 0) return;
        WaterPhase = phase;
        foreach (var (cell, variants) in _waterCells)
        {
            var name = variants[(phase + _waterOffset[cell]) % variants.Length];
            if (!Tileset.TileCoords.TryGetValue(name, out var coords)) continue;
            Ground.SetCell(cell, GroundSourceId, new Vector2I(coords.col, coords.row));
        }
    }

    /// <summary>Hash deterministik 0..1 dari koordinat + salt (sama dengan preview Python).</summary>
    private static float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            var h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13;
            h *= 0x5BD1E995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    /// <summary>Peta mini: 1 piksel = 1 tile, warnanya mengikuti jenis ground.</summary>
    private static Texture2D BuildMinimap(WorldData data)
    {
        var image = Image.CreateEmpty(data.Width, data.Height, false, Image.Format.Rgba8);
        image.Fill(new Color(0.16f, 0.2f, 0.16f));
        for (var row = 0; row < data.Height; row++)
        {
            for (var col = 0; col < data.Width; col++)
            {
                var name = data.TileNames[data.Ground[row][col]];
                var color = name.StartsWith("water", StringComparison.Ordinal) ? new Color(0.24f, 0.42f, 0.6f)
                    : name.StartsWith("bridge", StringComparison.Ordinal) ? new Color(0.58f, 0.42f, 0.26f)
                    : name.StartsWith("path", StringComparison.Ordinal) || name.StartsWith("road", StringComparison.Ordinal)
                        ? new Color(0.64f, 0.52f, 0.35f)
                    : name.StartsWith("stone", StringComparison.Ordinal) || name.StartsWith("floor", StringComparison.Ordinal)
                        ? new Color(0.55f, 0.55f, 0.58f)
                    : name.StartsWith("sand", StringComparison.Ordinal) ? new Color(0.8f, 0.72f, 0.52f)
                    : name.StartsWith("cave", StringComparison.Ordinal) ? new Color(0.12f, 0.12f, 0.15f)
                    : name.StartsWith("cliff", StringComparison.Ordinal) ? new Color(0.38f, 0.35f, 0.32f)
                    : new Color(0.3f, 0.48f, 0.28f);
                image.SetPixel(col, row, color);
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>SpriteFrames hero dari hero.json (sel 64x40, kaki di anchor).</summary>
    private SpriteFrames BuildHeroFrames()
    {
        var frames = new SpriteFrames();
        foreach (var (name, anim) in Hero.Animations)
        {
            frames.AddAnimation(name);
            frames.SetAnimationSpeed(name, Math.Max(1, anim.Fps));
            frames.SetAnimationLoop(name, anim.Loop);
            foreach (var frameName in anim.Frames)
            {
                if (!Hero.Frames.TryGetValue(frameName, out var frame)) continue;
                var atlas = new AtlasTexture
                {
                    Atlas = HeroTexture,
                    Region = new Rect2(frame.X, frame.Y, frame.W, frame.H),
                };
                frames.AddFrame(name, atlas);
            }
        }
        if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
        return frames;
    }

    private void BuildEnemyFrames()
    {
        foreach (var (enemyId, anims) in Enemies.PerEnemy)
        {
            var frames = new SpriteFrames();
            foreach (var (name, anim) in anims)
            {
                if (anim.Frames.Count == 0) continue;
                frames.AddAnimation(name);
                frames.SetAnimationSpeed(name, Math.Max(1, anim.Fps));
                frames.SetAnimationLoop(name, anim.Loop);
                foreach (var frameName in anim.Frames)
                {
                    if (!Enemies.Frames.TryGetValue(frameName, out var frame)) continue;
                    var atlas = new AtlasTexture
                    {
                        Atlas = EnemyTexture,
                        Region = new Rect2(frame.X, frame.Y, frame.W, frame.H),
                    };
                    frames.AddFrame(name, atlas);
                }
            }
            if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
            EnemyFrames[enemyId] = frames;
        }
    }

    private void BuildNpcFrames()
    {
        foreach (var (npcId, anims) in Npcs.PerNpc)
        {
            var frames = new SpriteFrames();
            foreach (var (name, anim) in anims)
            {
                if (anim.Frames.Count == 0) continue;
                frames.AddAnimation(name);
                frames.SetAnimationSpeed(name, Math.Max(1, anim.Fps));
                frames.SetAnimationLoop(name, anim.Loop);
                foreach (var frameName in anim.Frames)
                {
                    if (!Npcs.Frames.TryGetValue(frameName, out var frame)) continue;
                    frames.AddFrame(name, new AtlasTexture
                    {
                        Atlas = NpcTexture,
                        Region = new Rect2(frame.X, frame.Y, frame.W, frame.H),
                    });
                }
            }
            if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
            NpcFrames[npcId] = frames;
        }
    }

    /// <summary>NPC terdekat yang bisa diajak bicara (dalam jangkauan E).</summary>
    public NpcInfo? NpcNear(Vector2 pixel)
    {
        NpcInfo? best = null;
        var bestDistance = Rules.InteractRange;
        foreach (var npc in Data.Npcs)
        {
            var pos = new Vector2((npc.Col + 0.5f) * TileSize, (npc.Row + 1f) * TileSize);
            var distance = pos.DistanceTo(pixel);
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = npc;
        }
        return best;
    }

    public ZoneInfo? ZoneAt(Vector2 pixel) =>
        Data.ZoneAt((int)(pixel.X / TileSize), (int)(pixel.Y / TileSize));

    public CheckpointInfo? CheckpointNear(Vector2 pixel)
    {
        foreach (var cp in Data.Checkpoints)
        {
            var pos = new Vector2((cp.Col + 0.5f) * TileSize, (cp.Row + 0.5f) * TileSize);
            if (pos.DistanceTo(pixel) <= Rules.InteractRange)
                return cp;
        }
        return null;
    }
}
