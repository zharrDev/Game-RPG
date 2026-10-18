using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RpgGame;

/// <summary>
/// Tes headless: menjalankan kode game yang sama seperti dimainkan pemain
/// (bukan tiruan), lalu mencetak hasil dan keluar dengan exit code.
/// Jalankan: godot --headless --path game -- --test
/// </summary>
public static class HeadlessTest
{
    private static int _pass;
    private static readonly List<string> Failures = new();

    private static int bridgeColSample(World world)
    {
        for (var col = 0; col < world.Data.Width; col++)
        {
            if (world.Data.TileNames[world.Data.Ground[46][col]].StartsWith("bridge_deck")) return col;
        }
        return 70;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
        {
            _pass++;
            GD.Print($"ok    {name}");
        }
        else
        {
            Failures.Add(name + (detail.Length > 0 ? $" ({detail})" : ""));
            GD.Print($"FAIL  {name}  {detail}");
        }
    }

    /// <summary>Titik kaki pertama yang bisa dipijak di dalam zona bekas terbakar.</summary>
    private static Vector2 SproutSpot(World world, BurntZoneInfo zone)
    {
        for (var row = zone.Y; row < zone.Y + zone.H; row++)
        {
            for (var col = zone.X; col < zone.X + zone.W; col++)
            {
                var feet = new Vector2((col + 0.5f) * 16, (row + 1) * 16);
                if (world.Grid.WalkableAt(feet)) return feet;
            }
        }
        return new Vector2((zone.X + 0.5f) * 16, (zone.Y + 1) * 16);
    }

    public static void Run(Game game)
    {
        var world = game.World;
        var player = game.Player;
        if (world is null || player is null)
        {
            GD.PrintErr("FAIL  dunia gagal dibangun");
            game.GetTree().Quit(1);
            return;
        }

        GD.Print("=== aset & peta ===");
        Check("tileset dimuat (194 tile termasuk 16 fase air & 3 tanah hangus)",
            world.Tileset.Objects.Count == 194, $"{world.Tileset.Objects.Count}");
        Check("peta 96x72 tile", world.Data.Width == 96 && world.Data.Height == 72);
        Check("layer tanah terisi seluruh peta",
            world.GroundCellCount == world.Data.Width * world.Data.Height, $"{world.GroundCellCount}");
        var propNodes = world.PropRoot.GetChildren().OfType<Sprite2D>().Count();
        Check("semua prop terpasang sebagai sprite", propNodes == world.Data.Props.Count,
            $"{propNodes} vs {world.Data.Props.Count}");
        Check("prop punya y-sort (posisi = garis alas)",
            world.Root.YSortEnabled && world.PropRoot.GetChildCount() > 0);

        // data sel TileMapLayer benar-benar terpasang (bukan cuma dihitung)
        var samples = new (int col, int row)[6];
        samples[0] = (world.Data.Spawn.X, world.Data.Spawn.Y);
        samples[1] = (0, 0); samples[2] = (world.Data.Width - 1, 0);
        samples[3] = (0, world.Data.Height - 1); samples[4] = (bridgeColSample(world), 46);
        samples[5] = (48, 6);
        var missingCells = 0;
        foreach (var (col, row) in samples)
        {
            if (col < 0 || row < 0 || col >= world.Data.Width || row >= world.Data.Height) continue;
            if (world.Ground.GetCellSourceId(new Vector2I(col, row)) < 0) missingCells++;
        }
        Check("sel TileMapLayer benar-benar terisi (6 sampel)", missingCells == 0, $"{missingCells} kosong");

        // anchor sprite: kaki (foot) harus tepat di posisi node, jadi y-sort & bayangan akurat
        var heroSprite = player.GetNode<AnimatedSprite2D>("Sprite");
        Check("anchor kaki hero = posisi node",
            !heroSprite.Centered && heroSprite.Offset == new Vector2(-world.Hero.Foot.X, -world.Hero.Foot.Y),
            $"offset={heroSprite.Offset}");
        var mobSprite = game.Mobs.Count > 0 ? game.Mobs[0].GetNode<AnimatedSprite2D>("Sprite") : null;
        Check("anchor kaki musuh = posisi node",
            mobSprite is not null && !mobSprite.Centered &&
            mobSprite.Offset == new Vector2(-world.Enemies.Foot.X, -world.Enemies.Foot.Y),
            $"offset={mobSprite?.Offset}");

        GD.Print("=== kolisi ===");
        Check("spawn berdiri di tile walkable", world.Grid.WalkableAt(player.Position));
        Check("sungai tidak bisa dilewati", !world.Grid.Walkable(78, 20));
        Check("tile di luar peta dianggap tertutup", !world.Grid.Walkable(-1, 5) && !world.Grid.Walkable(96, 5));

        // dinding air: jalan ke utara dari tepi barat sungai tidak boleh menembus air
        var probe = new Player(world)
        {
            Position = new Vector2(70 * 16 + 8, 30 * 16 + 8),
        };
        var blocked = false;
        for (var i = 0; i < 240; i++)
        {
            probe.Tick(1.0 / 60, new InputState { Y = -1 }, world.Grid);
            if (!world.Grid.WalkableAt(probe.Position)) blocked = true;
        }
        Check("gerak ke utara tidak menembus sungai/tebing",
            !blocked && probe.Position.Y < 30 * 16, $"y={probe.Position.Y:F0}");
        probe.Free();

        // jembatan: harus bisa menyeberang dari tepi barat ke tepi timur
        var bridgeCols = new List<int>();
        for (var col = 0; col < world.Data.Width; col++)
        {
            var name = world.Data.TileNames[world.Data.Ground[46][col]];
            if (name.StartsWith("bridge_deck")) bridgeCols.Add(col);
        }
        Check("jembatan ada di peta (baris 46)", bridgeCols.Count >= 3, $"{bridgeCols.Count} tile");
        var cross = new Player(world)
        {
            Position = new Vector2((bridgeCols.Min() - 3) * 16 + 8, 46 * 16 + 14),
        };
        var stayedWalkable = true;
        for (var i = 0; i < 420; i++)
        {
            cross.Tick(1.0 / 60, new InputState { X = 1, Run = true }, world.Grid);
            if (!world.Grid.WalkableAt(cross.Position)) stayedWalkable = false;
        }
        Check("bisa menyeberangi sungai lewat jembatan",
            stayedWalkable && cross.Position.X > (bridgeCols.Max() + 1) * 16,
            $"x={cross.Position.X:F0} batas={(bridgeCols.Max() + 1) * 16}");
        cross.Free();
        player.Position = new Vector2((world.Data.Spawn.X + 0.5f) * 16, (world.Data.Spawn.Y + 1f) * 16);

        GD.Print("=== gerak & animasi 4 arah ===");
        var steps = new (string name, InputState input, string animation, bool flip)[]
        {
            ("bawah", new InputState { Y = 1 }, "walk_down", false),
            ("atas", new InputState { Y = -1 }, "walk_up", false),
            ("kanan", new InputState { X = 1 }, "walk_side", false),
            ("kiri", new InputState { X = -1 }, "walk_side", true),
        };
        foreach (var (name, input, animation, flip) in steps)
        {
            var actor = new Player(world) { Position = player.Position };
            for (var i = 0; i < 8; i++) actor.Tick(1.0 / 60, input, world.Grid);
            var sprite = actor.GetNode<AnimatedSprite2D>("Sprite");
            Check($"animasi {name} -> {animation}", sprite.Animation == animation, sprite.Animation);
            Check($"menghadap {name} flip={flip}", sprite.FlipH == flip);
            var moved = actor.Position.DistanceTo(player.Position) > 1;
            Check($"bergerak {name}", moved, $"delta={actor.Position.DistanceTo(player.Position):F1}");
            actor.Free();
        }
        var runner = new Player(world) { Position = player.Position };
        for (var i = 0; i < 8; i++) runner.Tick(1.0 / 60, new InputState { X = 1, Run = true }, world.Grid);
        Check("lari memakai animasi run_side",
            runner.GetNode<AnimatedSprite2D>("Sprite").Animation == "run_side",
            runner.GetNode<AnimatedSprite2D>("Sprite").Animation);
        runner.Free();

        GD.Print("=== aksi: serang & skill ===");
        var fighter = new Player(world) { Position = player.Position };
        var strikes = new List<(string kind, int damage)>();
        fighter.Strike += (_, _, damage, kind) => strikes.Add((kind, damage));
        var idleSprite = fighter.GetNode<AnimatedSprite2D>("Sprite");
        fighter.Tick(1.0 / 60, new InputState { AttackPressed = true }, world.Grid);
        Check("tekan serang -> animasi attack", fighter.Action == "attack" && idleSprite.Animation == "attack");
        for (var i = 0; i < 40; i++) fighter.Tick(1.0 / 60, default, world.Grid);
        Check("serangan menghasilkan satu hit", strikes.Count(s => s.kind == "attack") == 1, $"{strikes.Count}");
        Check("setelah serangan kembali idle", fighter.Action == "idle" && idleSprite.Animation == "idle_down");

        fighter.Tick(1.0 / 60, new InputState { JumpPressed = true }, world.Grid);
        Check("tekan lompat -> animasi jump", fighter.Action == "jump" && idleSprite.Animation == "jump",
            idleSprite.Animation);
        for (var i = 0; i < 40; i++) fighter.Tick(1.0 / 60, default, world.Grid);
        Check("selesai lompat kembali idle", fighter.Action == "idle" && idleSprite.Animation == "idle_down",
            idleSprite.Animation);
        Check("lompat memakai 3 frame", idleSprite.SpriteFrames!.GetFrameCount("jump") == 3);

        var mortal = new Player(world) { Position = player.Position };
        var mortalSprite = mortal.GetNode<AnimatedSprite2D>("Sprite");
        var mortalDied = mortal.TakeDamage(9999);
        Check("HP habis -> pemain mati", mortalDied && !mortal.Alive);
        Check("kematian memutar animasi death (bukan diam)",
            mortal.Action == "death" && mortalSprite.Animation == "death", mortalSprite.Animation);
        Check("animasi death memakai 3 frame", mortalSprite.SpriteFrames!.GetFrameCount("death") == 3);
        for (var i = 0; i < 120; i++) mortal.Tick(1.0 / 60, default, world.Grid);
        Check("timer kematian mencapai durasi bangkit", mortal.DeathTimer >= Rules.DeathDuration,
            $"{mortal.DeathTimer:F2} detik");
        mortal.Free();

        strikes.Clear();
        fighter.Tick(1.0 / 60, new InputState { SkillPressed = true }, world.Grid);
        for (var i = 0; i < 40; i++) fighter.Tick(1.0 / 60, default, world.Grid);
        Check("skill terkunci di level 1", strikes.Count == 0 && fighter.State.Level == 1);

        // naik ke level 3 lewat XP
        fighter.AddXp(500);
        Check("naik level dari XP", fighter.State.Level >= 3, $"level {fighter.State.Level}");
        Check("skill terbuka di level 3", fighter.SkillUnlocked && fighter.State.SkillLevel >= 1);
        var energyBefore = fighter.Energy;
        fighter.Tick(1.0 / 60, new InputState { SkillPressed = true }, world.Grid);
        for (var i = 0; i < 60; i++) fighter.Tick(1.0 / 60, default, world.Grid);
        Check("skill level 3 mengeluarkan aura slash", strikes.Any(s => s.kind == "skill"),
            string.Join(",", strikes.Select(s => s.kind)));
        Check("skill memakai energi", fighter.Energy < energyBefore, $"{fighter.Energy:F1} < {energyBefore:F1}");
        Check("damage skill > damage serangan dasar",
            strikes.Where(s => s.kind == "skill").Select(s => s.damage).DefaultIfEmpty(0).Max() >
            Rules.AttackDamage(fighter.State.Level));
        fighter.Free();

        GD.Print("=== tempur: musuh kena, mati, XP & kill ===");
        var mobs = game.Mobs;
        Check("musuh ter-spawn sesuai peta", mobs.Count == world.Data.Enemies.Count, $"{mobs.Count}");
        Check("tiga tipe musuh dipakai",
            mobs.Select(m => m.Stats.Id).Distinct().OrderBy(x => x).SequenceEqual(new[] { "bat", "slime", "warden" }),
            string.Join(",", mobs.Select(m => m.Stats.Id).Distinct()));
        Check("sprite boss dari frame AI terpasang",
            world.EnemyFrames["warden"].GetFrameCount("idle") == 2);

        var victim = mobs.First(m => m.Stats.Id == "slime");
        victim.Position = player.Position + new Vector2(20, 0);
        var hpBefore = victim.Hp;
        var killsBefore = player.State.Kills;
        var xpBefore = player.State.Xp;
        var attacker = new Player(world);
        attacker.Strike += game.ApplyStrike;   // jalur damage yang sama dengan gameplay
        attacker.Position = victim.Position - new Vector2(16, 0);
        attacker.State.Facing = 1;
        attacker.Facing = "right";
        attacker.Tick(1.0 / 60, new InputState { AttackPressed = true }, world.Grid);
        for (var i = 0; i < 30; i++) attacker.Tick(1.0 / 60, default, world.Grid);
        Check("serangan mengurangi HP musuh", victim.Hp < hpBefore, $"{victim.Hp} < {hpBefore}");

        for (var i = 0; i < 12 && !victim.Dead; i++)
        {
            attacker.AttackTimer = 0;
            attacker.Tick(1.0 / 60, new InputState { AttackPressed = true }, world.Grid);
            for (var j = 0; j < 30; j++) attacker.Tick(1.0 / 60, default, world.Grid);
        }
        Check("musuh bisa dikalahkan", victim.Dead);
        Check("musuh memutar animasi death",
            victim.GetNode<AnimatedSprite2D>("Sprite").Animation == "death",
            victim.GetNode<AnimatedSprite2D>("Sprite").Animation);
        for (var i = 0; i < 70; i++) game.MobsTickForTest(1.0 / 60);
        Check("kill & XP tercatat", player.State.Kills > killsBefore && player.State.Xp >= xpBefore,
            $"kills {player.State.Kills} xp {player.State.Xp}");
        Check("korban tercatat di world.defeated", player.State.Defeated.GetValueOrDefault("slime") >= 1);
        attacker.Free();

        GD.Print("=== zona & checkpoint ===");
        var zones = new (int col, int row, string id)[]
        {
            (50, 40, "greenwood_vale"), (48, 8, "north_woods"),
            (88, 46, "old_road"), (14, 14, "ancient_ruins"),
        };
        foreach (var (col, row, id) in zones)
        {
            var zone = world.ZoneAt(new Vector2(col * 16 + 8, row * 16 + 8));
            Check($"zona di ({col},{row}) = {id}", zone?.Id == id, zone?.Id ?? "null");
        }
        foreach (var cp in world.Data.Checkpoints)
        {
            var pos = new Vector2((cp.Col + 0.5f) * 16, (cp.Row + 0.5f) * 16);
            Check($"checkpoint {cp.Id} bisa didekati", world.CheckpointNear(pos)?.Id == cp.Id);
        }

        // istirahat (tombol E): satu-satunya cara memulihkan HP di luar naik level
        var camp = world.Data.Checkpoints[0];
        player.Position = new Vector2((camp.Col + 0.5f) * 16, (camp.Row + 0.5f) * 16);
        player.State.Hp = 10;
        player.Energy = 5;
        var rested = game.RestAtCheckpoint();
        Check("E di checkpoint memulihkan HP penuh", rested && player.State.Hp == player.State.MaxHp,
            $"{player.State.Hp}/{player.State.MaxHp}");
        Check("istirahat mengisi energi penuh", Math.Abs(player.Energy - Rules.MaxEnergy) < 0.01, $"{player.Energy:F1}");
        player.Position = new Vector2(70 * 16 + 8, 30 * 16 + 8);
        Check("istirahat di luar checkpoint ditolak", !game.RestAtCheckpoint());

        GD.Print("=== NPC, dialog & quest ===");
        Check("lima warga desa ter-spawn", game.Npcs.Count == 5, $"{game.Npcs.Count}");
        Check("NPC punya sprite idle 2 frame",
            world.NpcFrames.TryGetValue("mira", out var miraFrames) && miraFrames.GetFrameCount("idle_down") == 2);
        Check("NPC punya walk 4 frame (bekal warga berjalan)",
            world.NpcFrames.TryGetValue("tomas", out var tomasFrames) &&
            tomasFrames.GetFrameCount("walk_down") == 4 && tomasFrames.GetFrameCount("walk_side") == 4);
        var npcFrame = world.NpcFrames["mira"].GetFrameTexture("idle_down", 0) as AtlasTexture;
        Check("frame NPC menunjuk npc_atlas.png (384x400)",
            npcFrame is not null && npcFrame.Atlas == world.NpcTexture &&
            world.NpcTexture.GetWidth() == 384 && world.NpcTexture.GetHeight() == 400,
            $"{world.NpcTexture.GetWidth()}x{world.NpcTexture.GetHeight()}");

        var mira = world.Data.Npcs.First(n => n.Id == "mira");
        player.Position = new Vector2((mira.Col - 1 + 0.5f) * 16, (mira.Row + 1f) * 16);
        Check("NPC terdeteksi dalam jangkauan E", world.NpcNear(player.Position)?.Id == "mira");
        player.State.Defeated["slime"] = 0;
        player.State.Flags.Remove("quest_slime_done");
        Check("quest Mira tertunda (penanda !)",
            game.QuestPending(mira) && game.Npcs.First(n => n.Info.Id == "mira").QuestPending);
        var markerLabel = game.Npcs.First(n => n.Info.Id == "mira").GetNode<Label>("QuestMarker");
        Check("penanda ! memakai font pixel",
            Hud.PixelFont is not null && markerLabel.GetThemeFont("font") == Hud.PixelFont);
        Check("E membuka dialog", game.Interact() && game.DialogOpen, game.TalkingTo ?? "");
        Check("HUD menampilkan nama pembicara", game.Hud!.DialogSpeaker.Contains("Mira"), game.Hud.DialogSpeaker);
        Check("baris pertama = kalimat NPC", game.DialogLine == mira.Lines[0], game.DialogLine);
        var lastLine = "";
        for (var i = 0; i < 8 && game.DialogOpen; i++)
        {
            lastLine = game.DialogLine;
            game.AdvanceDialog();
        }
        Check("dialog ditutup setelah baris terakhir", !game.DialogOpen);
        Check("baris terakhir = tawaran quest", lastLine == mira.Quest!.Intro, lastLine);

        player.State.Defeated["slime"] = mira.Quest.Count;
        var questLevelBefore = player.State.Level;
        var questXpBefore = player.State.Xp;
        Check("E membuka dialog lagi", game.Interact() && game.DialogOpen);
        Check("quest selesai -> flag tersimpan", player.State.Flags.GetValueOrDefault("quest_slime_done"));
        Check("hadiah quest menambah XP/level",
            player.State.Level != questLevelBefore || player.State.Xp != questXpBefore,
            $"lv {player.State.Level} xp {player.State.Xp}");
        Check("penanda quest hilang setelah selesai", !game.QuestPending(mira));
        while (game.DialogOpen) game.AdvanceDialog();
        Check("dialog quest ditutup rapi", !game.DialogOpen);

        GD.Print("=== visual dunia: bayangan & obor ===");
        var shadowedProps = world.PropRoot.GetChildren().OfType<Sprite2D>()
            .Count(sprite => sprite.GetChildren().OfType<Sprite2D>().Any(child => child.Name == "AmbientShadow"));
        Check("bayangan ambient di bawah pohon/bangunan", shadowedProps >= 50, $"{shadowedProps} prop");
        Check("obor & lentera berkedip terdaftar", world.FlickerProps.Count >= 2, $"{world.FlickerProps.Count}");

        GD.Print("=== pipeline FX: asap, bloom, grading, heat haze ===");
        var fx = game.Fx;
        Check("layer FX terpasang", fx is not null);
        Check("shader asap/bloom/grading/heat termuat",
            fx is not null && fx.SmokeMaterial.Shader is not null && fx.GlowMaterial.Shader is not null &&
            fx.GradeMaterial.Shader is not null && fx.HeatMaterial.Shader is not null);
        Check("heat haze terpasang di setiap titik api",
            fx is not null && world.HeatSources.Count >= 2 && fx.HeatSourceCount == world.HeatSources.Count,
            $"{fx?.HeatSourceCount ?? -1} vs {world.HeatSources.Count}");
        Check("ketebalan asap di rentang aman",
            fx is not null && fx.AshDensity > 0f && fx.AshDensity <= 0.75f, $"{fx?.AshDensity ?? -1f}");
        Check("uniform kamera asap terpasang (asap mengikuti dunia)",
            fx is not null &&
            fx.SmokeMaterial.GetShaderParameter("cam_px").VariantType == Variant.Type.Vector2 &&
            fx.SmokeMaterial.GetShaderParameter("noise_tex").VariantType == Variant.Type.Object,
            fx is null ? "fx null" : fx.SmokeMaterial.GetShaderParameter("cam_px").VariantType.ToString());

        GD.Print("=== lighting siang/malam & cahaya api ===");
        var lighting = game.Lighting;
        Check("node lighting terpasang", lighting is not null);
        if (lighting is not null)
        {
            Check("satu cahaya per titik api",
                world.HeatSources.Count >= 2 && lighting.Lights.Count == world.HeatSources.Count,
                $"{lighting.Lights.Count} vs {world.HeatSources.Count}");
            var lightsOk = lighting.Lights.Count > 0;
            for (var i = 0; i < lighting.Lights.Count && lightsOk; i++)
            {
                var (pos, _, _) = world.HeatSources[i];
                lightsOk = lighting.Lights[i].Texture is not null &&
                           lighting.Lights[i].BlendMode == Light2D.BlendModeEnum.Add &&
                           lighting.Lights[i].Position.DistanceTo(pos) < 0.01f;
            }
            Check("cahaya api: tekstur, mode add, posisi pas", lightsOk);

            lighting.SetTimeOfDay(0.5f);
            var noonColor = lighting.Modulate.Color;
            var noonEnergy = lighting.Lights.Count > 0 ? lighting.Lights[0].Energy : 0f;
            Check("tengah hari terang, malam = 0",
                lighting.NightAmount < 0.05f && noonColor.R > 0.95f && noonColor.B > 0.95f,
                $"night={lighting.NightAmount:0.00} {noonColor}");

            lighting.SetTimeOfDay(0f);
            var nightColor = lighting.Modulate.Color;
            var nightEnergy = lighting.Lights.Count > 0 ? lighting.Lights[0].Energy : 0f;
            Check("tengah malam gelap kebiruan, malam penuh",
                lighting.NightAmount > 0.95f && nightColor.B > nightColor.R && nightColor.R < 0.6f,
                $"night={lighting.NightAmount:0.00} {nightColor}");
            Check("nyala api lebih kuat saat malam", nightEnergy > noonEnergy,
                $"{nightEnergy:0.00} vs {noonEnergy:0.00}");

            lighting.SetTimeOfDay(0.98f);
            lighting.Advance(lighting.DayLength * 0.05);
            Check("jam berputar & membungkus", MathF.Abs(lighting.TimeOfDay - 0.03f) < 0.01f,
                $"t={lighting.TimeOfDay:0.000}");

            fx?._Process(0.0);
            Check("uniform night grading ikut siklus",
                fx is not null &&
                MathF.Abs((float)fx.GradeMaterial.GetShaderParameter("night") - lighting.NightAmount) < 0.001f,
                fx is null ? "fx null" : fx.GradeMaterial.GetShaderParameter("night").ToString());
        }

        GD.Print("=== api, bara, abu & tunas (dekoratif) ===");
        var ambience = game.Ambience;
        Check("ambience api terpasang", ambience is not null);
        Check("zona bekas terbakar terdefinisi di peta", world.Data.BurntZones.Count >= 2,
            $"{world.Data.BurntZones.Count} zona");
        if (ambience is not null)
        {
            Check("nyala api satu per titik api & beranimasi",
                ambience.Flames.Count == world.HeatSources.Count && ambience.Flames.Count >= 2 &&
                ambience.Flames.TrueForAll(f => f.IsPlaying() && f.SpriteFrames?.HasAnimation("burn") == true),
                $"{ambience.Flames.Count} vs {world.HeatSources.Count}");
            Check("pool partikel dialokasikan sekali (kapasitas tetap)",
                ambience.Embers?.Capacity == FireAmbience.EmberCapacity &&
                ambience.Ash?.Capacity == FireAmbience.AshCapacity &&
                ambience.Embers.ActiveCount == 0 && ambience.Ash!.ActiveCount == 0,
                $"ember {ambience.Embers?.Capacity}, ash {ambience.Ash?.Capacity}");

            // bara: muncul, lalu kembali ke pool setelah umurnya habis
            // (kamera dijauhkan dulu supaya tidak ada bara otomatis dari api yang terlihat)
            ambience.SetCameraCenter(new Vector2(-1000f, -1000f));
            var emberSpawned = ambience.ForceEmber(new Vector2(560, 480));
            ambience.Advance(0.4);
            Check("bara hidup & masih di udara sebelum umurnya habis",
                emberSpawned && ambience.Embers!.ActiveCount == 1, $"{ambience.Embers.ActiveCount}");
            ambience.Advance(3.0);
            Check("pool bara bersih lagi setelah umur habis", ambience.Embers.ActiveCount == 0,
                $"{ambience.Embers.ActiveCount}");

            // tunas: tumbuh di dalam zona, lalu memudar & kembali ke pool
            var zone = world.Data.BurntZones[0];
            var sproutSpot = SproutSpot(world, zone);
            var sproutSprite = ambience.ForceSproutAt(sproutSpot);
            ambience.Advance(1.0);
            Check("tunas tumbuh (skala membesar)",
                sproutSprite is not null && sproutSprite.Scale.X > 0.6f && ambience.SproutCount == 1,
                sproutSprite is null ? "penuh" : $"{sproutSprite.Scale.X:0.00}");
            ambience.Advance(5.0);
            Check("tunas selesai & kembali ke pool", ambience.SproutCount == 0, $"{ambience.SproutCount}");

            // abu hanya saat kamera di zona terbakar; asap menebal di sana
            var inside = new Vector2((zone.X + zone.W / 2f) * 16, (zone.Y + zone.H / 2f) * 16);
            ambience.SetCameraCenter(inside);
            ambience.Advance(1.0);
            Check("abu beterbangan saat kamera di zona terbakar",
                ambience.Ash!.ActiveCount > 0 &&
                MathF.Abs(ambience.AshBoost - FireAmbience.BurntAshBoost) < 0.001f,
                $"{ambience.Ash.ActiveCount} abu, boost {ambience.AshBoost:0.00}");
            ambience.SetCameraCenter(new Vector2(48 * 16, 36 * 16));
            ambience.Advance(0.2);
            Check("di luar zona: boost asap nol", ambience.AshBoost == 0f, $"{ambience.AshBoost:0.00}");
        }

        GD.Print("=== peti, gold & potion ===");
        Check("peti harta ter-spawn sesuai peta", world.Data.Chests.Count >= 4, $"{world.Data.Chests.Count}");
        var chest = world.Data.Chests[0];
        var chestSpot = new Vector2((chest.Col + 0.5f) * 16, (chest.Row + 1.5f) * 16);
        if (!world.Grid.WalkableAt(chestSpot))
            chestSpot = new Vector2((chest.Col + 0.5f) * 16, (chest.Row - 0.5f) * 16);
        player.Position = chestSpot;
        player.State.Flags.Remove($"chest_{chest.Id}");
        Check("peti terdeteksi dalam jangkauan E", game.ChestNear(player.Position)?.Id == chest.Id,
            game.ChestNear(player.Position)?.Id ?? "null");
        var goldBeforeChest = player.State.Gold;
        Check("E membuka peti & memberi gold",
            game.OpenChest(chest) && player.State.Gold == goldBeforeChest + chest.Gold,
            $"{goldBeforeChest} -> {player.State.Gold}");
        Check("flag peti tersimpan", game.ChestOpened(chest));
        Check("peti tidak bisa dibuka dua kali",
            !game.OpenChest(chest) && player.State.Gold == goldBeforeChest + chest.Gold);
        var chestOpenTile = world.Tileset.Objects["chest_open"];
        var chestTexture = world.PropSpriteAt(chest.Col, chest.Row)?.Texture as AtlasTexture;
        Check("sprite peti berganti ke terbuka",
            chestTexture is not null && chestTexture.Region.Position == new Vector2(chestOpenTile.X, chestOpenTile.Y),
            chestTexture?.Region.Position.ToString() ?? "null");

        var chest2 = world.Data.Chests[1];
        var chestSpot2 = new Vector2((chest2.Col + 0.5f) * 16, (chest2.Row + 1.5f) * 16);
        if (!world.Grid.WalkableAt(chestSpot2))
            chestSpot2 = new Vector2((chest2.Col + 0.5f) * 16, (chest2.Row - 0.5f) * 16);
        player.Position = chestSpot2;
        Check("tombol E lewat Interact() membuka peti", game.Interact() && game.ChestOpened(chest2));

        player.State.Potions = 2;
        player.State.Hp = 30;
        Check("minum potion memulihkan HP & mengurangi stok",
            game.DrinkPotion() && player.State.Hp == 30 + Rules.PotionHeal && player.State.Potions == 1,
            $"hp {player.State.Hp} potion {player.State.Potions}");
        player.State.Hp = player.State.MaxHp;
        Check("potion ditolak saat HP penuh", !game.DrinkPotion() && player.State.Potions == 1);
        player.State.Potions = 0;
        Check("potion habis ditolak", !game.DrinkPotion());

        player.State.Gold = 100;
        Check("beli potion memotong gold",
            game.BuyPotion() && player.State.Gold == 100 - Rules.PotionPrice && player.State.Potions == 1,
            $"gold {player.State.Gold}");
        player.State.Gold = 5;
        Check("beli potion ditolak kalau gold kurang",
            !game.BuyPotion() && player.State.Gold == 5 && player.State.Potions == 1);
        player.State.Gold = 0;
        player.State.Potions = 0;

        GD.Print("=== animasi air & peta mini ===");
        Check("sel air terdaftar untuk animasi", world.WaterCells.Count > 200, $"{world.WaterCells.Count}");
        var shoreCells = world.WaterCells.Where(pair => pair.Value.Length == 2)
            .Select(pair => pair.Key).OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToList();
        var shoreBefore = shoreCells.ToDictionary(cell => cell, cell => world.Ground.GetCellAtlasCoords(cell));
        var rippleSpread = shoreCells.Take(24).Select(world.WaterVariantIndex).Distinct().Count();
        Check("riak air tidak seragam sejak awal (tidak pop serentak)", rippleSpread >= 2, $"{rippleSpread} varian");
        world.SetWaterPhase(1);
        Check("fase air 1 mengganti tampilan semua sel pantai",
            shoreCells.All(cell => world.Ground.GetCellAtlasCoords(cell) != shoreBefore[cell]));
        world.SetWaterPhase(2);
        Check("fase air kembali ke tile awal",
            shoreCells.All(cell => world.Ground.GetCellAtlasCoords(cell) == shoreBefore[cell]));
        world.SetWaterPhase(0);

        GD.Print("=== tanah hangus (overlay dekoratif) ===");
        Check("tanah hangus dipasang di zona terbakar",
            world.Scorched is not null && world.ScorchedCellCount >= 90, $"{world.ScorchedCellCount}");
        var scorchCells = world.Scorched?.GetUsedCells() ?? new Godot.Collections.Array<Vector2I>();
        Check("sel hangus semuanya di dalam zona terbakar",
            scorchCells.Count > 0 && scorchCells.All(cell =>
                world.Data.BurntZones.Any(zone =>
                    cell.X >= zone.X && cell.X < zone.X + zone.W &&
                    cell.Y >= zone.Y && cell.Y < zone.Y + zone.H)),
            $"{scorchCells.Count} sel");
        Check("tanah hangus hanya menimpa grass/meadow (jalan & air tetap bersih)",
            scorchCells.All(cell =>
            {
                var groundName = world.Data.TileNames[world.Data.Ground[cell.Y][cell.X]];
                return groundName.StartsWith("grass", StringComparison.Ordinal) ||
                       groundName.StartsWith("meadow", StringComparison.Ordinal);
            }));
        Check("peta tetap penuh setelah animasi air",
            world.Ground.GetUsedCells().Count == world.Data.Width * world.Data.Height,
            $"{world.Ground.GetUsedCells().Count}");
        Check("minimap 96x72 dibangun dari data peta",
            world.Minimap is not null && world.Minimap.GetWidth() == 96 && world.Minimap.GetHeight() == 72);
        Check("HUD menampilkan minimap", game.Hud!.MinimapVisible);

        GD.Print("=== game feel: shake, hit-stop, debu & warga berjalan ===");
        var feel = game.Feel;
        Check("pool debu & percikan siap",
            feel is not null && feel.Dust is not null && feel.Sparks is not null &&
            (feel.Dust?.Capacity ?? 0) >= 32 && (feel.Sparks?.Capacity ?? 0) >= 16,
            feel is null ? "null" : $"{feel.Dust?.Capacity}/{feel.Sparks?.Capacity}");
        if (feel is not null && feel.Dust is not null && feel.Sparks is not null)
        {
            var dust = feel.Dust;
            var sparks = feel.Sparks;
            for (var i = 0; i < 40; i++) feel.Advance(1.0 / 40);   // bersihkan efek dari tes tempur
            Check("kamera tenang setelah efek mereda",
                feel.ShakeAmount == 0f && feel.CameraOffset == Vector2.Zero && !feel.Frozen);

            feel.Shake(3f);
            Check("shake aktif setelah dipanggil", MathF.Abs(feel.ShakeAmount - 3f) < 0.001f, $"{feel.ShakeAmount:F2}");
            feel.Advance(0.2);
            Check("shake mereda seiring waktu", feel.ShakeAmount < 3f, $"{feel.ShakeAmount:F2}");
            feel.Shake(99f);
            feel.Advance(1.0 / 60);
            Check("offset kamera dibatasi supaya tidak mual",
                feel.CameraOffset.Length() <= GameFeel.MaxCameraOffset + 0.01f, $"{feel.CameraOffset.Length():F2}");
            feel.Advance(12);   // habiskan shake besar

            feel.HitStop(0.08);
            Check("hit-stop membekukan dunia", feel.Frozen);
            feel.Advance(0.05);
            Check("hit-stop masih berjalan setelah 50 ms", feel.Frozen, $"{feel.HitStopRemaining:F3}s");
            feel.Advance(0.05);
            Check("hit-stop berakhir setelah waktunya", !feel.Frozen);

            var liveMob = game.Mobs.FirstOrDefault(m => !m.Dead && GodotObject.IsInstanceValid(m));
            if (liveMob is not null)
            {
                var mobHp = liveMob.Hp;
                var sparksBefore = feel.SparkSpawned;
                game.ApplyStrike(liveMob.Position, "down", 1, "attack");
                Check("serangan kena otomatis memicu shake + hit-stop + percikan",
                    liveMob.Hp < mobHp && feel.HitStopRemaining > 0 && feel.ShakeAmount > 0f &&
                    feel.SparkSpawned > sparksBefore,
                    $"hp {mobHp}->{liveMob.Hp} stop {feel.HitStopRemaining:F3}s shake {feel.ShakeAmount:F1}");
                for (var i = 0; i < 40; i++) feel.Advance(1.0 / 40);
            }

            var dustBefore = feel.DustSpawned;
            feel.StepPuff(player.Position, true);
            Check("debu langkah muncul (2 partikel saat lari)",
                feel.DustSpawned - dustBefore == 2 && dust.ActiveCount >= 2,
                $"{feel.DustSpawned - dustBefore} partikel");
            var sparkBefore = feel.SparkSpawned;
            feel.Burst(player.Position, new Color(1f, 0.85f, 0.38f), 8);
            Check("percikan peti muncul (8 partikel)",
                feel.SparkSpawned - sparkBefore == 8 && sparks.ActiveCount >= 8);
            for (var i = 0; i < 60; i++) feel.Advance(1.0 / 60);
            Check("partikel habis umurnya lalu kembali ke pool",
                dust.ActiveCount == 0 && sparks.ActiveCount == 0,
                $"debu {dust.ActiveCount} percikan {sparks.ActiveCount}");
            for (var i = 0; i < 120; i++) feel.StepPuff(player.Position, false);
            Check("pool debu tidak pernah melebihi kapasitas",
                dust.ActiveCount <= dust.Capacity, $"{dust.ActiveCount}/{dust.Capacity}");
            for (var i = 0; i < 60; i++) feel.Advance(1.0 / 60);

            var walker = game.Npcs.First(n => n.Info.Id == "tomas");
            var home = walker.Home;
            var startedWalking = false;
            for (var i = 0; i < 60 * 8 && !startedWalking; i++)
            {
                walker.Tick(1.0 / 60, false);
                startedWalking = walker.Position != home;
            }
            Check("warga mulai mondar-mandir sendiri", startedWalking,
                $"x {walker.Position.X - home.X:F1}px");
            Check("warga memakai animasi walk_side saat berjalan",
                walker.Walking && walker.Animation == "walk_side", walker.Animation);
            for (var i = 0; i < 60 * 60; i++) walker.Tick(1.0 / 60, false);
            Check("warga tidak menjauh dari rumahnya (± 2.2 tile, sumbu Y tetap)",
                MathF.Abs(walker.Position.X - home.X) <= Npc.WanderRadiusTiles * World.TileSize + 1f &&
                MathF.Abs(walker.Position.Y - home.Y) < 0.01f,
                $"dx {walker.Position.X - home.X:F1}px");
            var pausedPos = walker.Position;
            for (var i = 0; i < 60; i++) walker.Tick(1.0 / 60, true);
            Check("warga berhenti saat dialog/menu terbuka", walker.Position == pausedPos);
        }

        GD.Print("=== HUD 2g: jam, tracker quest, vignette & ringkasan menu ===");
        var hud = game.Hud!;
        hud.UpdateClock(0.5f);
        Check("jam HUD: t=0.5 -> Siang 12:00", hud.ClockText == "Siang 12:00", hud.ClockText);
        hud.UpdateClock(0.0f);
        Check("jam HUD: tengah malam", hud.ClockText == "Malam 00:00", hud.ClockText);
        hud.UpdateClock(0.75f);
        Check("jam HUD: fase senja menjelang malam",
            hud.ClockText.StartsWith("Senja") && hud.ClockText.EndsWith("18:00"), hud.ClockText);
        hud.UpdateClock(0.95f);
        Check("jam HUD: malam larut", hud.ClockText == "Malam 22:48", hud.ClockText);

        var trackerNpc = world.Data.Npcs.FirstOrDefault(n => n.Quest is not null && n.Quest.Flag.Length > 0);
        if (trackerNpc?.Quest is not null)
        {
            var trackerQuest = trackerNpc.Quest;
            var hadFlag = player.State.Flags.GetValueOrDefault(trackerQuest.Flag);
            player.State.Flags.Remove(trackerQuest.Flag);
            player.State.Defeated[trackerQuest.Target] = 0;
            game.RefreshQuestTracker();
            Check("tracker quest tampil dengan progres 0/N",
                hud.QuestTrackerText.Contains(trackerNpc.Name) && hud.QuestTrackerText.Contains("0/"),
                hud.QuestTrackerText.Replace('\n', ' '));
            player.State.Defeated[trackerQuest.Target] = trackerQuest.Count;
            game.RefreshQuestTracker();
            Check("tracker quest berubah saat target tercapai",
                hud.QuestTrackerText.Contains("selesai"), hud.QuestTrackerText.Replace('\n', ' '));
            if (hadFlag) player.State.Flags[trackerQuest.Flag] = true;
            game.RefreshQuestTracker();
            Check("tracker quest hilang setelah flag selesai",
                !hud.QuestTrackerText.Contains(trackerNpc.Name), hud.QuestTrackerText.Replace('\n', ' '));
        }
        else
        {
            Check("tracker quest: ada quest untuk diuji", false, "tidak ada NPC dengan quest");
        }

        hud.FlashDamage();
        hud.AdvanceOverlay(0.0, 0f);
        Check("kedip merah saat pemain terluka",
            hud.DamageFlashRemaining > 0 && hud.VignetteAlpha > 0.5f, $"{hud.VignetteAlpha:F2}");
        hud.AdvanceOverlay(1.0, 0f);
        Check("kedip terluka mereda (HP penuh -> vignette nol)",
            hud.DamageFlashRemaining == 0 && hud.VignetteAlpha == 0f, $"{hud.VignetteAlpha:F2}");
        player.State.Hp = Math.Max(1, player.State.MaxHp / 10);
        hud.UpdateStats(player.State, 50, 90, "greenwood_vale", "village_square");
        hud.AdvanceOverlay(0.0, 0.3f);
        Check("denyut vignette saat HP < 30%", hud.VignetteAlpha >= 0.2f, $"{hud.VignetteAlpha:F2}");
        player.State.Hp = player.State.MaxHp;
        hud.UpdateStats(player.State, 50, 90, "greenwood_vale", "village_square");
        hud.AdvanceOverlay(0.0, 0f);
        Check("vignette mati saat HP pulih", hud.VignetteAlpha == 0f, $"{hud.VignetteAlpha:F2}");

        game.OpenPauseMenu();
        Check("menu jeda terbuka + ringkasan profil",
            hud.MenuOpen && hud.PauseSummaryText.Contains("Lv") && hud.PauseSummaryText.Contains("tumbang"),
            hud.PauseSummaryText);
        game.CloseMenus();
        Check("menu jeda bisa ditutup", !hud.MenuOpen);

        GD.Print("=== audio chiptune ===");
        Check("manifest audio dimuat (9 SFX + 2 musik)", AudioLibrary.Count >= 11, $"{AudioLibrary.Count}");
        var slashCue = AudioLibrary.Get("slash");
        Check("SFX slash: 16-bit 22050 Hz & ada datanya",
            slashCue is not null && slashCue.MixRate == 22050 &&
            slashCue.Format == AudioStreamWav.FormatEnum.Format16Bits && slashCue.Data.Length > 1000,
            slashCue is null ? "null" : $"{slashCue.MixRate}Hz {slashCue.Data.Length}B");
        var musicCue = AudioLibrary.Get("music_village");
        Check("musik desa loop di stream yang sama",
            musicCue is not null && musicCue.LoopMode == AudioStreamWav.LoopModeEnum.Forward && musicCue.LoopEnd > 0,
            musicCue is null ? "null" : $"loopEnd={musicCue.LoopEnd}");
        Check("pemutar audio siap", game.Sfx is not null);
        if (game.Sfx is not null)
        {
            Check("SFX bisa diputar", game.Sfx.Play("hit") && game.Sfx.LastCue == "hit", game.Sfx.LastCue);
            Check("cue tak dikenal ditolak", !game.Sfx.Play("tidak_ada_cue"));
            Check("musik zona bisa diganti",
                game.Sfx.PlayMusic("music_wilds") && game.Sfx.MusicCue == "music_wilds");
            game.Sfx.PlayMusic("music_village");
        }

        GD.Print("=== save/load lewat backend C# ===");
        var api = game.Api!;
        if (!api.Healthy(out var serverVersion))
        {
            Check("backend bisa dihubungi", false, api.LastError ?? "tidak ada respon");
        }
        else
        {
            Check("backend bisa dihubungi", true, $"v{serverVersion}");
            var name = $"headless-{DateTime.UtcNow:HHmmss}";
            var profile = api.CreateProfile(name);
            Check("bisa membuat profil", profile is not null && profile.Id.Length > 0, api.LastError ?? "");
            if (profile is not null)
            {
                game.ProfileId = profile.Id;   // seperti StartProfile di gameplay
                // ubah keadaan PERMAINAN yang sebenarnya (bukan salinan), lalu simpan
                player.AddXp(500);                       // naik level 4 -> skillLevel & aura_slash ikut terisi
                var state = player.State;
                state.Hp = 77;
                state.Gold = 123;
                state.Potions = 3;
                state.Kills = 7;
                state.Deaths = 2;
                state.Checkpoint = "bridge_camp";
                game.ZoneId = "old_road";
                game.Playtime = 321;
                player.Position = new Vector2(1200.5f, 700.25f);
                var levelBefore = state.Level;
                var info = api.Save(profile.Id, 2, game.BuildState());
                Check("simpan ke slot 2 lewat klien game", info is not null && info.Slot == 2, api.LastError ?? "");

                state.Hp = 1; state.Xp = 0; state.Kills = 0; state.Zone = "greenwood_vale";
                var loaded = api.Load(profile.Id, 2);
                Check("muat slot 2 mengembalikan data", loaded is not null);
                if (loaded is not null)
                {
                    Check("level tersimpan", loaded.Level == levelBefore, $"{loaded.Level} vs {levelBefore}");
                    Check("posisi tersimpan", Math.Abs(loaded.X - 1200.5) < 0.01 && Math.Abs(loaded.Y - 700.25) < 0.01,
                        $"{loaded.X},{loaded.Y}");
                    Check("zona & checkpoint tersimpan",
                        loaded.Zone == "old_road" && loaded.Checkpoint == "bridge_camp");
                    Check("playtime & kill tersimpan", Math.Abs(loaded.Playtime - 321) < 0.1 && loaded.Kills == 7,
                    $"playtime {loaded.Playtime} kill {loaded.Kills}");
                    Check("skill aura_slash tersimpan", loaded.Skills.Contains("aura_slash"));
                    Check("gold & potion tersimpan di server",
                        loaded.Gold == 123 && loaded.Potions == 3, $"gold {loaded.Gold} potion {loaded.Potions}");
                }
                var resume = api.Resume(profile.Id);
                Check("resume mengembalikan slot terakhir",
                    resume?.Slot == 2 && resume?.State.Level == levelBefore && resume?.State.Zone == "old_road");

                // simpan otomatis saat keluar (tombol Keluar / jendela ditutup)
                state.Hp = 61;
                var exitSaved = game.SaveOnExit();
                var exitLoaded = api.Load(profile.Id, game.CurrentSlot);
                Check("simpan otomatis saat keluar", exitSaved && exitLoaded?.Hp == 61,
                    $"{exitLoaded?.Hp}");

                var invalids = api.Save(profile.Id, 99, game.BuildState());
                Check("slot di luar 1-3 ditolak server", invalids is null);

                Check("profil uji dibersihkan", api.DeleteProfile(profile.Id));
            }
            game.ProfileId = null;

            GD.Print("=== sinkronisasi simpanan offline -> server ===");
            var syncPath = $"user://test-sync-{Time.GetTicksMsec()}.json";
            var syncStore = new LocalSaveStore(syncPath);
            var syncProfile = syncStore.CreateProfile($"OfflineSync-{Time.GetTicksMsec()}");
            if (syncProfile is not null)
            {
                syncStore.Save(syncProfile.Id, 1,
                    new GameState { Hp = 64, MaxHp = 120, Level = 2, X = 321, Y = 654, Zone = "old_road" });
                var moved = api.SyncOfflineToServer(syncStore);
                Check("profil offline dipindahkan ke server", moved == 1, $"{moved}");
                var serverProfile = api.ListProfiles().FirstOrDefault(p => p.Name == syncProfile.Name);
                var syncedSlot = serverProfile is null ? null : api.Load(serverProfile.Id, 1);
                Check("slot hasil sinkronisasi utuh di server",
                    syncedSlot is not null && syncedSlot.Hp == 64 && syncedSlot.Zone == "old_road",
                    syncedSlot is null ? "null" : $"{syncedSlot.Hp}");
                Check("data lokal dibersihkan setelah sinkron",
                    syncStore.ListProfiles().Count == 0 && !FileAccess.FileExists(syncPath));
                if (serverProfile is not null) api.DeleteProfile(serverProfile.Id);
                syncStore.DeleteFile();
            }
        }

        GD.Print("=== sprite ===");
        var heroAnims = new[] { "idle_down", "walk_down", "run_down", "idle_up", "walk_up", "idle_side", "walk_side",
            "attack", "skill", "hurt", "death", "jump" };
        var missingAnim = heroAnims.Where(a => !world.HeroFrames.HasAnimation(a)).ToList();
        Check("semua animasi hero ada", missingAnim.Count == 0, string.Join(",", missingAnim));
        var emptyAnim = heroAnims.Where(a => world.HeroFrames.GetFrameCount(a) == 0).ToList();
        Check("tidak ada animasi hero kosong", emptyAnim.Count == 0, string.Join(",", emptyAnim));
        Check("hero memakai 53 frame", world.Hero.Frames.Count == 53, $"{world.Hero.Frames.Count}");
        Check("enemy atlas memuat slime, bat, warden",
            world.EnemyFrames.ContainsKey("slime") && world.EnemyFrames.ContainsKey("bat") &&
            world.EnemyFrames.ContainsKey("warden"));
        var maxX = world.Hero.Frames.Values.Max(f => f.X + f.W);
        Check("region sprite di dalam atlas", maxX <= 512, $"{maxX}");

        // regresi: frame harus menunjuk atlas yang benar (dulu memakai tileset.png)
        Check("hero memakai hero_atlas.png (512x280)",
            world.HeroTexture.GetWidth() == 512 && world.HeroTexture.GetHeight() == 280,
            $"{world.HeroTexture.GetWidth()}x{world.HeroTexture.GetHeight()}");
        var heroAtlas = world.HeroFrames.GetFrameTexture("idle_down", 0) as AtlasTexture;
        Check("frame hero menunjuk hero_atlas.png", heroAtlas is not null && heroAtlas.Atlas == world.HeroTexture);
        var bossAtlas = world.EnemyFrames["warden"].GetFrameTexture("idle", 0) as AtlasTexture;
        Check("frame musuh menunjuk enemy_atlas.png (320x160)",
            bossAtlas is not null && bossAtlas.Atlas == world.EnemyTexture &&
            world.EnemyTexture.GetWidth() == 320 && world.EnemyTexture.GetHeight() == 160,
            $"{world.EnemyTexture.GetWidth()}x{world.EnemyTexture.GetHeight()}");
        var playerAtlas = player.GetNode<AnimatedSprite2D>("Sprite").SpriteFrames!.GetFrameTexture("idle_down", 0) as AtlasTexture;
        Check("sprite pemain memakai atlas hero yang sama", playerAtlas is not null && playerAtlas.Atlas == world.HeroTexture);

        GD.Print("=== HUD pixel & simpanan lokal ===");
        Check("font pixel HUD termuat", Hud.PixelFont is not null);
        if (Hud.PixelFont is not null)
        {
            Check("tinggi font pixel 8 px", Hud.PixelFont.GetHeight(8) >= 7, $"{Hud.PixelFont.GetHeight(8)}");
            Check("lebar teks = 6 px/glyph", Math.Abs(Hud.PixelFont.GetStringSize("ARGA").X - 24) < 0.01,
                $"{Hud.PixelFont.GetStringSize("ARGA").X}");
        }

        var localPath = $"user://test-offline-{Time.GetTicksMsec()}.json";
        var local = new LocalSaveStore(localPath);
        local.DeleteFile();   // mulai bersih walau ada sisa dari run sebelumnya
        var localProfile = local.CreateProfile("Offline Tester");
        Check("profil lokal dibuat tanpa server",
            localProfile is not null && localProfile.Id.StartsWith("local-"), localProfile?.Id ?? "null");
        if (localProfile is not null)
        {
            var offlineState = new GameState { Hp = 55, MaxHp = 130, Level = 3, X = 123.5, Y = 456.25, Zone = "north_woods" };
            offlineState.Defeated["slime"] = 4;
            var localSlot = local.Save(localProfile.Id, 2, offlineState);
            Check("simpan lokal menulis slot", localSlot is not null && localSlot.Slot == 2 && localSlot.Level == 3);
            var localBack = local.Load(localProfile.Id, 2);
            Check("muat lokal mengembalikan state utuh",
                localBack is not null && localBack.Hp == 55 && localBack.Zone == "north_woods" &&
                Math.Abs(localBack.X - 123.5) < 0.01 && localBack.Defeated["slime"] == 4);
            var localResume = local.Resume(localProfile.Id);
            Check("resume lokal memakai slot terakhir", localResume?.Slot == 2 && localResume?.State.Level == 3);
            local.AddEvent(localProfile.Id, "offline", "tes");
            Check("catatan perjalanan lokal tercatat", local.Journey(localProfile.Id).Any(e => e.Kind == "offline"));
            Check("hapus profil lokal", local.DeleteProfile(localProfile.Id) &&
                local.ListProfiles().All(p => p.Id != localProfile.Id));
            Check("file simpanan lokal bisa dihapus",
                local.DeleteFile() && !FileAccess.FileExists(localPath), localPath);
        }

        var deadApi = new SaveApi("http://127.0.0.1:1", new LocalSaveStore($"user://test-offline-api-{Time.GetTicksMsec()}.json"));
        Check("backend mati terdeteksi", !deadApi.Healthy(out _) && deadApi.Offline);
        var deadProfile = deadApi.CreateProfile("Tanpa Server");
        Check("SaveApi pindah ke simpanan lokal", deadProfile is not null && deadProfile.Id.StartsWith("local-"));
        if (deadProfile is not null)
        {
            var deadSaved = deadApi.Save(deadProfile.Id, 1, new GameState { Hp = 42, MaxHp = 120 });
            var deadLoaded = deadApi.Load(deadProfile.Id, 1);
            Check("simpan & muat lewat SaveApi offline", deadSaved is not null && deadLoaded?.Hp == 42,
                $"{(deadLoaded is null ? "null" : deadLoaded.Hp.ToString())}");
            deadApi.DeleteProfile(deadProfile.Id);
            deadApi.LocalStore.DeleteFile();
        }

        GD.Print("");
        GD.Print($"=== hasil: {_pass} lulus, {Failures.Count} gagal ===");
        foreach (var failure in Failures) GD.Print($"  GAGAL: {failure}");
        game.GetTree().Quit(Failures.Count == 0 ? 0 : 1);
    }
}
