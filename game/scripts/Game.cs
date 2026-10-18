using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RpgGame;

/// <summary>
/// Root game (dipasang di scenes/Main.tscn). Membangun dunia dari data hasil
/// tools/generate_*.py, menjalankan loop permainan, dan menyambung save/load ke
/// backend C# (server/RpgServer).
/// </summary>
public sealed partial class Game : Node2D
{
    public World? World { get; private set; }
    public Player? Player { get; private set; }
    public Hud? Hud { get; private set; }
    public Fx? Fx { get; private set; }
    public Lighting? Lighting { get; private set; }
    public FireAmbience? Ambience { get; private set; }
    public GameFeel? Feel { get; private set; }
    public AudioPlayer? Sfx { get; private set; }
    public SaveApi? Api { get; private set; }

    public readonly List<Mob> Mobs = new();
    public readonly List<Npc> Npcs = new();
    private readonly List<string> _dialog = new();
    private Npc? _dialogNpc;
    private int _dialogIndex;

    /// <summary>Kotak dialog sedang terbuka (permainan dijeda).</summary>
    public bool DialogOpen => _dialog.Count > 0;
    public string DialogLine => _dialogIndex < _dialog.Count ? _dialog[_dialogIndex] : "";
    public string? TalkingTo => _dialogNpc?.Info.Id;
    public string? ProfileId;
    public string ProfileName = "";
    public int CurrentSlot = 1;
    public string ZoneId = "";
    public string ZoneName = "";
    public double AutosaveTimer;
    public bool TestMode;

    private readonly CameraRig _cameraRig = new();
    private readonly TimeSystem _time = new();
    private bool _menuVisible;
    private bool _testPending;
    private double _stepTimer;
    private bool _airborne;
    private int _questTrackerSignature = -1;
    private string? _shotPath;
    private double _shotDelay = 1.5;
    private double _shotTimer;
    private float _shotAsh = -1f;
    private float _startTime = 0.5f;
    private float _dayLength = 300f;
    private float _ashBase = 0.20f;

    /// <summary>Waktu bermain (detik) — disimpan ke stats.playtime.</summary>
    public double Playtime { get; set; }
    private string _serverUrl = "http://127.0.0.1:5080";

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--server=")) _serverUrl = arg["--server=".Length..];
            else if (arg.StartsWith("--profile=")) ProfileId = arg["--profile=".Length..];
            else if (arg.StartsWith("--slot=") && int.TryParse(arg["--slot=".Length..], out var slot)) CurrentSlot = slot;
            else if (arg.StartsWith("--shot=")) _shotPath = arg["--shot=".Length..];
            else if (arg.StartsWith("--shot-delay=") &&
                     double.TryParse(arg["--shot-delay=".Length..], System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var shotDelay)) _shotDelay = shotDelay;
            else if (arg.StartsWith("--shot-ash=") &&
                     float.TryParse(arg["--shot-ash=".Length..], System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var shotAsh)) _shotAsh = shotAsh;
            else if (arg.StartsWith("--time=") &&
                     float.TryParse(arg["--time=".Length..], System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var startTime)) _startTime = startTime;
            else if (arg.StartsWith("--day-length=") &&
                     float.TryParse(arg["--day-length=".Length..], System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var dayLength)) _dayLength = dayLength;
            else if (arg == "--test") TestMode = true;
        }

        Hud.EnsurePixelFont();   // sebelum BuildWorld: NPC memakai font pixel untuk penanda "!"
        SetupInput();
        BuildWorld();

        // pipeline FX (asap, bloom, grading) + heat haze di tiap titik api
        Fx = new Fx();
        AddChild(Fx);
        AttachFireFx();

        // siklus siang/malam + cahaya hangat di tiap titik api
        Lighting = new Lighting { DayLength = _dayLength };
        World.Root.AddChild(Lighting);
        Lighting.Configure(World.HeatSources, Assets.LoadTexture(Assets.FxLightSoftPng), Fx);
        Lighting.SetTimeOfDay(_startTime);
        _time.Configure(Lighting);

        // ambience api & pemulihan: nyala 4-frame, bara, abu, tunas (murni visual)
        Ambience = new FireAmbience();
        World.Root.AddChild(Ambience);
        Ambience.Configure(World);

        Api = new SaveApi(_serverUrl);
        var version = "";
        var backendUp = Api.Healthy(out version);
        if (!backendUp && !TestMode)
            GD.PrintErr($"[game] backend {_serverUrl} tidak bisa dihubungi: {Api.LastError}");
        else if (backendUp)
            GD.Print($"[game] backend v{version} di {_serverUrl}");

        Hud = new Hud();
        AddChild(Hud);
        Hud.SaveSlot += SaveToSlot;
        Hud.LoadSlot += LoadFromSlot;
        Hud.CloseMenu += CloseMenus;
        Hud.QuitToDesktop += () =>
        {
            SaveOnExit();
            GetTree().Quit(0);
        };
        Hud.BackToProfiles += () => { CloseMenus(); ShowProfiles(); };
        Hud.ProfilePicked += StartProfile;
        Hud.ProfileCreated += CreateProfile;
        Hud.BuyPotion += () => BuyPotion();

        Sfx = new AudioPlayer();
        AddChild(Sfx);
        Sfx.PlayMusic("music_village");

        if (World is not null) Hud.SetMinimap(World.Minimap);

        if (Api.Offline && !TestMode)
        {
            Hud.Toast($"Backend {_serverUrl} mati — mode offline: progres disimpan di mesin ini.");
        }
        else if (!TestMode)
        {
            // server hidup: pindahkan profil/slot yang sempat tersimpan offline
            var imported = Api.SyncOfflineToServer(Api.LocalStore);
            if (imported > 0)
                Hud.Toast($"{imported} profil dari simpanan offline dipindahkan ke server.");
        }

        if (_shotAsh >= 0f) _ashBase = Math.Clamp(_shotAsh, 0f, 0.75f);
        if (Fx is not null) Fx.AshDensity = _ashBase;

        if (TestMode)
        {
            _testPending = true;
            return;
        }

        if (_shotPath is not null)
        {
            // mode screenshot: dunia tampil apa adanya (tanpa menu profil), lalu disimpan
            GD.Print($"[shot] menunggu {_shotDelay:0.0} dtk lalu menyimpan ke {_shotPath}");
            return;
        }

        if (ProfileId is not null) StartProfile(ProfileId);
        else ShowProfiles();
    }

    // ------------------------------------------------------------------- setup

    private void SetupInput()
    {
        Register("move_left", Key.A, Key.Left);
        Register("move_right", Key.D, Key.Right);
        Register("move_up", Key.W, Key.Up);
        Register("move_down", Key.S, Key.Down);
        Register("run", Key.Shift);
        Register("attack", Key.J);
        Register("jump", Key.Space);
        Register("skill", Key.K);
        Register("potion", Key.Q);
        Register("interact", Key.E);
        Register("quick_save", Key.F);
        Register("menu", Key.Escape);
    }

    private static void Register(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        else InputMap.ActionEraseEvents(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    private void BuildWorld()
    {
        var tileset = TilesetData.Load(Assets.TilesetJson);
        var data = WorldData.Load(Assets.WorldJson);
        World = World.Build(data, tileset, useTilemapGround: true);
        AddChild(World.Root);

        Player = new Player(World)
        {
            Position = new Vector2((data.Spawn.X + 0.5f) * World.TileSize, (data.Spawn.Y + 1f) * World.TileSize),
        };
        Player.State.MaxHp = Rules.MaxHp(Player.State.Level);
        Player.State.Hp = Player.State.MaxHp;
        Player.State.Zone = data.SpawnZone;
        Player.State.Checkpoint = data.Checkpoints.Count > 0 ? data.Checkpoints[0].Id : "village_square";
        Player.Strike += ApplyStrike;
        Player.LeveledUp += OnLevelUp;
        World.Root.AddChild(Player);

        _cameraRig.Build(Player, data.PixelSize);

        // game feel (tahap 2f): shake kamera, hit-stop, debu langkah, percikan
        Feel = new GameFeel();
        Feel.Configure();
        World.Root.AddChild(Feel);

        SpawnMobs();
        SpawnNpcs();
        UpdateZone(true);
    }

    /// <summary>
    /// Pasang heat haze di semua titik api dunia. Quad-nya ditaruh di node
    /// terpisah yang tidak ikut y-sort supaya shimmer selalu di depan sprite.
    /// </summary>
    private void AttachFireFx()
    {
        if (World is null || Fx is null) return;
        var heatRoot = new Node2D { Name = "HeatHaze", YSortEnabled = false };
        World.Root.AddChild(heatRoot);
        foreach (var (pos, phase, size) in World.HeatSources)
            Fx.AttachHeat(heatRoot, pos, size, phase);
    }

    private void SpawnNpcs()
    {
        if (World is null) return;
        foreach (var info in World.Data.Npcs)
        {
            var npc = new Npc(World, info);
            Npcs.Add(npc);
            World.Root.AddChild(npc);
        }
        RefreshNpcMarkers();
    }

    /// <summary>True kalau NPC ini punya quest yang belum selesai (penanda "!").</summary>
    public bool QuestPending(NpcInfo info)
    {
        var quest = info.Quest;
        if (quest is null || quest.Flag.Length == 0 || Player is null) return false;
        return !Player.State.Flags.GetValueOrDefault(quest.Flag);
    }

    private void RefreshNpcMarkers()
    {
        if (Player is null) return;
        foreach (var npc in Npcs) npc.SetQuestPending(QuestPending(npc.Info));
    }

    private void SpawnMobs()
    {
        if (World is null) return;
        var id = 0;
        foreach (var spawn in World.Data.Enemies)
        {
            var mob = new Mob(World, spawn, id++);
            mob.Removed += OnMobRemoved;
            Mobs.Add(mob);
            World.Root.AddChild(mob);
        }
    }

    /// <summary>Catat peristiwa ke catatan perjalanan (dilakukan server, bukan client).</summary>
    public void LogEvent(string kind, string detail)
    {
        if (ProfileId is null) return;
        Api?.AddEvent(ProfileId, kind, detail);
    }

    private void OnMobRemoved(Mob mob)
    {
        Mobs.Remove(mob);
        Player!.State.Defeated[mob.Stats.Id] = Player.State.Defeated.GetValueOrDefault(mob.Stats.Id) + 1;
        Player.State.Kills++;
        Player.AddXp(mob.Stats.Xp);
        Hud?.Toast($"{mob.Stats.Display} tumbang  ·  +{mob.Stats.Xp} XP");
        if (mob.Stats.Boss)
        {
            Hud?.Toast($"{mob.Stats.Display} dikalahkan!");
            LogEvent("boss_defeated", mob.Stats.Display);
            Autosave("boss kalah");
        }
    }

    private void OnLevelUp(int level)
    {
        Sfx?.Play("levelup");
        Hud?.Toast($"Naik level {level}! HP penuh, skill Aura Slash siap.");
        LogEvent("level_up", $"level {level}");
    }

    // ------------------------------------------------------------------ update

    public override void _Process(double delta)
    {
        if (_testPending)
        {
            _testPending = false;
            try
            {
                HeadlessTest.Run(this);
            }
            catch (Exception ex)
            {
                // jangan biarkan tes menggantung: laporkan lalu keluar dengan kode gagal
                GD.PrintErr($"FAIL  tes headless berhenti karena error: {ex}");
                GetTree().Quit(1);
            }
            return;
        }

        if (_shotPath is not null)
        {
            _shotTimer += delta;
            if (_shotTimer >= _shotDelay)
            {
                CaptureShot();
                return;
            }
        }

        if (World is null || Player is null || Hud is null) return;
        // game feel: shake mereda, hit-stop berjalan, partikel menua
        Feel?.Advance(delta);
        if (Feel is not null) _cameraRig.ApplyOffset(Feel.CameraOffset);
        var frozen = Feel?.Frozen == true;
        // asap yang menempel di dunia: ikut titik tengah kamera (termasuk smoothing)
        var camCenter = _cameraRig.ScreenCenter;
        Fx?.SetCameraCenter(camCenter);
        // jam dunia + denyut visual lingkungan (kedip obor, riak air)
        _time.Advance(delta, World);
        // ambience api: nyala, bara, abu zona terbakar, tunas (murni visual)
        Ambience?.SetCameraCenter(camCenter);
        Ambience?.Advance(delta);
        if (Fx is not null && Ambience is not null) Fx.AshDensity = _ashBase + Ambience.AshBoost;

        var menuPressed = Input.IsActionJustPressed("menu");
        if (menuPressed)
        {
            if (Hud.ProfileMenuOpen) { }
            else if (Hud.MenuOpen) CloseMenus();
            else OpenPauseMenu();
        }
        if (Input.IsActionJustPressed("quick_save") && ProfileId is not null) SaveToSlot(CurrentSlot);

        if (DialogOpen && Input.IsActionJustPressed("interact")) AdvanceDialog();

        var playing = ProfileId is not null && !Hud.MenuOpen && !Hud.ProfileMenuOpen && !DialogOpen;
        if (playing && Input.IsActionJustPressed("interact") && !Interact())
            Hud.Toast("Tidak ada yang bisa diajak bicara di dekat sini");
        if (playing && Input.IsActionJustPressed("potion")) DrinkPotion();
        if (playing && Player.Alive && !frozen)
        {
            Player.Tick(delta, ReadInput(), World.Grid);
            if (Player.JumpTimer > 0)
            {
                _airborne = true;
            }
            else if (_airborne)
            {
                _airborne = false;
                Feel?.LandingPuff(Player.Position);
            }
            if (Player.Action is "walk" or "run")
            {
                _stepTimer += delta;
                if (_stepTimer >= (Player.Action == "run" ? 0.24 : 0.34))
                {
                    _stepTimer = 0;
                    Sfx?.Play("step", -20f);
                    Feel?.StepPuff(Player.Position, Player.Action == "run");
                }
            }
            else
            {
                _stepTimer = 0;
            }
            Playtime += delta;
            Player.State.Playtime = Playtime;
            AutosaveTimer += delta;
            if (AutosaveTimer >= Rules.AutosaveInterval)
            {
                AutosaveTimer = 0;
                Autosave("autosave");
            }
            UpdateZone(false);
            UpdateCheckpoint();
        }
        else if (playing && !frozen)
        {
            Player.Tick(delta, default, World.Grid);
        }

        if (!frozen)
        {
            foreach (var mob in Mobs.ToArray())
            {
                if (!IsInstanceValid(mob)) continue;
                mob.Tick(delta, Player.Center, Player.Alive && playing, World.Grid, OnMobHitsPlayer);
            }
        }

        // warga mondar-mandir singkat (tahap 2f, murni visual); berhenti saat dialog/menu/hit-stop
        foreach (var npc in Npcs)
        {
            if (!IsInstanceValid(npc)) continue;
            npc.Tick(delta, !playing || frozen || DialogOpen || !Player.Alive);
        }

        if (playing && !Player.Alive && Player.DeathTimer >= Rules.DeathDuration)
            Respawn();

        Hud.UpdateStats(Player.State, Player.Energy, Playtime, ZoneName, Player.State.Checkpoint);
        Hud.UpdateMinimap(Player.Position, World.Data.PixelSize);
        Hud.UpdateClock(Lighting?.TimeOfDay ?? 0.5f);
        RefreshQuestTracker();
    }

    /// <summary>
    /// Simpan satu frame viewport ke PNG (mode `--shot=`, butuh layar — headless
    /// tidak merender). Dipakai untuk dokumentasi tahap visual ke docs/.
    /// </summary>
    private void CaptureShot()
    {
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(_shotPath!);
        if (error == Error.Ok)
        {
            GD.Print($"[shot] tersimpan {_shotPath} ({image.GetWidth()}x{image.GetHeight()})");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"[shot] gagal menyimpan {_shotPath}: {error} (headless tidak merender)");
            GetTree().Quit(1);
        }
    }

    private InputState ReadInput() => new()
    {
        X = (Input.IsActionPressed("move_right") ? 1 : 0) - (Input.IsActionPressed("move_left") ? 1 : 0),
        Y = (Input.IsActionPressed("move_down") ? 1 : 0) - (Input.IsActionPressed("move_up") ? 1 : 0),
        Run = Input.IsActionPressed("run"),
        AttackPressed = Input.IsActionJustPressed("attack"),
        SkillPressed = Input.IsActionJustPressed("skill"),
        JumpPressed = Input.IsActionJustPressed("jump"),
    };

    private void UpdateZone(bool force)
    {
        if (World is null || Player is null) return;
        var zone = World.ZoneAt(Player.Position);
        var id = zone?.Id ?? ZoneId;
        var name = zone?.Name ?? ZoneName;
        if (!force && id == ZoneId) return;
        var first = ZoneId.Length == 0;
        ZoneId = id;
        ZoneName = name;
        Player.State.Zone = id;
        Sfx?.PlayMusic(id == "greenwood_vale" ? "music_village" : "music_wilds");
        if (!first && Player.State.Zone != null)
        {
            Hud?.Toast($"Masuk zona: {name}");
            LogEvent("zone", name);
            Autosave("pindah zona");
        }
    }

    private void UpdateCheckpoint()
    {
        if (Player is null || World is null) return;
        var checkpoint = World.CheckpointNear(Player.Position);
        if (checkpoint is null || checkpoint.Id == Player.State.Checkpoint) return;
        Player.State.Checkpoint = checkpoint.Id;
        Hud?.Toast($"Checkpoint: {checkpoint.Name} — perjalanan disimpan");
        LogEvent("checkpoint", checkpoint.Name);
        Autosave("checkpoint");
    }

    /// <summary>Aksi tombol E: bicara dengan NPC terdekat; kalau tidak ada, istirahat di checkpoint.</summary>
    public bool Interact()
    {
        if (World is null || Player is null || !Player.Alive) return false;
        var info = World.NpcNear(Player.Position);
        if (info is not null)
        {
            OpenDialog(info);
            return true;
        }
        var chest = ChestNear(Player.Position);
        if (chest is not null)
        {
            if (ChestOpened(chest)) Hud?.Toast($"Peti {chest.Id} sudah kosong.");
            else OpenChest(chest);
            return true;
        }
        return RestAtCheckpoint();
    }

    // ------------------------------------------------------------- peti & item

    public ChestInfo? ChestNear(Vector2 position)
    {
        if (World is null) return null;
        ChestInfo? best = null;
        var bestDistance = Rules.InteractRange;
        foreach (var chest in World.Data.Chests)
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
        Player?.State.Flags.GetValueOrDefault($"chest_{chest.Id}") ?? false;

    /// <summary>Buka peti: gold + 1 potion, flag tersimpan supaya tidak bisa dicuri dua kali.</summary>
    public bool OpenChest(ChestInfo chest)
    {
        if (Player is null || ChestOpened(chest)) return false;
        Player.State.Flags[$"chest_{chest.Id}"] = true;
        Player.State.Gold += chest.Gold;
        Player.State.Potions = Math.Min(Rules.MaxPotions, Player.State.Potions + 1);
        World?.SetPropTile(chest.Col, chest.Row, "chest_open");
        Sfx?.Play("quest");
        // percikan emas dari mulut peti (visual, tahap 2f)
        var chestPos = new Vector2((chest.Col + 0.5f) * World.TileSize, (chest.Row + 0.5f) * World.TileSize);
        Feel?.Burst(chestPos - new Vector2(0, 4), new Color(1f, 0.85f, 0.38f, 0.95f), 10);
        Hud?.Toast($"Peti terbuka: +{chest.Gold} gold, +1 potion");
        LogEvent("chest", chest.Id);
        Autosave("peti terbuka");
        return true;
    }

    /// <summary>Minum potion (tombol Q).</summary>
    public bool DrinkPotion()
    {
        if (Player is null || !Player.Alive) return false;
        if (Player.State.Potions <= 0)
        {
            Hud?.Toast("Tidak ada potion. Beli di menu (Esc) kalau gold cukup.");
            return false;
        }
        if (Player.State.Hp >= Player.State.MaxHp)
        {
            Hud?.Toast("HP masih penuh — potion disimpan.");
            return false;
        }
        Player.State.Potions--;
        Player.State.Hp = Math.Min(Player.State.MaxHp, Player.State.Hp + Rules.PotionHeal);
        Sfx?.Play("rest");
        Hud?.Toast($"Potion: +{Rules.PotionHeal} HP · sisa {Player.State.Potions}");
        return true;
    }

    /// <summary>Beli potion di pasar (tombol di menu pause).</summary>
    public bool BuyPotion()
    {
        if (Player is null) return false;
        if (Player.State.Potions >= Rules.MaxPotions)
        {
            Hud?.Toast($"Persediaan potion penuh ({Rules.MaxPotions}).");
            return false;
        }
        if (Player.State.Gold < Rules.PotionPrice)
        {
            Hud?.Toast($"Gold tidak cukup — butuh {Rules.PotionPrice}, punya {Player.State.Gold}.");
            return false;
        }
        Player.State.Gold -= Rules.PotionPrice;
        Player.State.Potions++;
        Sfx?.Play("quest");
        Hud?.Toast($"Beli potion: -{Rules.PotionPrice} gold · sisa {Player.State.Gold}");
        Autosave("beli potion");
        return true;
    }

    public void OpenDialog(NpcInfo info)
    {
        if (Player is null) return;
        _dialog.Clear();
        _dialog.AddRange(info.Lines);
        var quest = info.Quest;
        if (quest is not null && quest.Flag.Length > 0)
        {
            if (!Player.State.Flags.GetValueOrDefault(quest.Flag))
            {
                var reached = Player.State.Defeated.GetValueOrDefault(quest.Target) >= quest.Count;
                if (reached)
                {
                    _dialog.Add(quest.DoneLine);
                    GrantQuestReward(quest, info);
                }
                else
                {
                    _dialog.Add(quest.Intro);
                }
            }
            else
            {
                _dialog.Add("Terima kasih sudah menolong desa ini.");
            }
        }

        _dialogNpc = Npcs.FirstOrDefault(n => n.Info.Id == info.Id);
        _dialogNpc?.Face(Player.Position);
        _dialogIndex = 0;
        if (_dialog.Count == 0)
        {
            Hud?.Toast($"{info.Name} hanya tersenyum.");
            return;
        }
        ShowDialogLine();
    }

    private void GrantQuestReward(NpcQuestSpec quest, NpcInfo giver)
    {
        if (Player is null) return;
        Player.State.Flags[quest.Flag] = true;
        Sfx?.Play("quest");
        Player.AddXp(quest.RewardXp);
        Hud?.Toast($"Quest selesai: {giver.Name} — +{quest.RewardXp} XP");
        LogEvent("quest", quest.Flag);
        Autosave("quest selesai");
        RefreshNpcMarkers();
    }

    private void ShowDialogLine()
    {
        if (_dialogNpc is null) return;
        Hud?.ShowDialog($"{_dialogNpc.Info.Name} — {_dialogNpc.Info.Role}", DialogLine);
    }

    public void AdvanceDialog()
    {
        if (!DialogOpen) return;
        _dialogIndex++;
        if (_dialogIndex >= _dialog.Count) CloseDialog();
        else ShowDialogLine();
    }

    private void CloseDialog()
    {
        _dialog.Clear();
        _dialogNpc = null;
        _dialogIndex = 0;
        Hud?.HideDialog();
        RefreshNpcMarkers();
    }

    /// <summary>Tombol E di checkpoint: istirahat memulihkan HP & energi, lalu simpan.</summary>
    public bool RestAtCheckpoint()
    {
        if (World is null || Player is null || !Player.Alive) return false;
        var checkpoint = World.CheckpointNear(Player.Position);
        if (checkpoint is null) return false;
        Player.State.Hp = Player.State.MaxHp;
        Player.Energy = Rules.MaxEnergy;
        Sfx?.Play("rest");
        Hud?.Toast($"Beristirahat di {checkpoint.Name} — HP & energi pulih");
        Autosave("istirahat");
        return true;
    }

    private void OnMobHitsPlayer(Mob mob, int damage)
    {
        if (Player is null || !Player.Alive) return;
        var died = Player.TakeDamage(damage);
        Sfx?.Play(died ? "death" : "hurt");
        Feel?.Shake(died ? 5.5f : 4f);
        Feel?.HitStop(0.085);
        Feel?.Burst(Player.Center, new Color(1f, 0.42f, 0.36f, 0.95f), 6);
        Hud?.FlashDamage();
        Hud?.Toast($"{mob.Stats.Display} menyerangmu  ·  -{damage} HP");
        if (died)
        {
            Player.State.Deaths++;
            LogEvent("death", $"dikalahkan {mob.Stats.Display} di {ZoneName}");
        }
    }

    private void Respawn()
    {
        if (Player is null || World is null) return;
        var checkpoint = World.Data.Checkpoints.FirstOrDefault(c => c.Id == Player.State.Checkpoint)
                         ?? World.Data.Checkpoints.FirstOrDefault();
        var pos = checkpoint is null
            ? new Vector2((World.Data.Spawn.X + 0.5f) * World.TileSize, (World.Data.Spawn.Y + 1f) * World.TileSize)
            : new Vector2((checkpoint.Col + 0.5f) * World.TileSize, (checkpoint.Row + 1f) * World.TileSize);
        Player.Position = pos;
        Player.Action = "idle";
        Player.DeathTimer = 0;
        Player.JumpTimer = 0;
        Player.State.Hp = Math.Max(1, Player.State.MaxHp / 2);
        CloseDialog();
        Player.SyncFromState();
        Hud?.Toast("Kau tumbang... bangkit di checkpoint terakhir.");
        Autosave("kebangkitan");
    }

    /// <summary>Selesaikan serangan pemain: cari musuh yang kena, kurangi HP.</summary>
    public void ApplyStrike(Vector2 origin, string facing, int damage, string kind)
    {
        if (World is null) return;
        var dir = facing switch
        {
            "left" => new Vector2(-1, 0),
            "right" => new Vector2(1, 0),
            "up" => new Vector2(0, -1),
            _ => new Vector2(0, 1),
        };
        var range = kind == "skill" ? Rules.SkillRange : Rules.AttackRange;
        Sfx?.Play(kind == "skill" ? "skill" : "slash");
        var hit = 0;
        Vector2? firstHit = null;
        foreach (var mob in Mobs.ToArray())
        {
            if (mob.Dead || !IsInstanceValid(mob)) continue;
            var offset = mob.Position - origin;
            var distance = offset.Length();
            if (distance > range) continue;
            if (kind != "skill" && distance > 0.5f && dir.Dot(offset.Normalized()) < Rules.AttackArcDot) continue;
            hit++;
            firstHit ??= mob.Position - new Vector2(0, 8);
            var died = mob.TakeDamage(damage);
            var label = kind == "skill" ? "Aura Slash" : "Serangan";
            Hud?.Toast($"{label}: {mob.Stats.Display} -{damage} HP" + (died ? " (tumbang)" : ""));
        }
        if (hit > 0)
        {
            Sfx?.Play("hit");
            Feel?.Shake(kind == "skill" ? 3.2f : 2.4f);
            Feel?.HitStop(0.055);
            if (firstHit is not null) Feel?.Burst(firstHit.Value, new Color(1f, 0.86f, 0.5f, 0.95f), 6);
        }
        if (hit == 0 && kind == "skill") Hud?.Toast("Aura Slash: tidak ada musuh di jangkauan");
    }

    /// <summary>Tick semua musuh (dipakai tes headless supaya timer kematian berjalan).</summary>
    public void MobsTickForTest(double delta)
    {
        if (World is null || Player is null) return;
        foreach (var mob in Mobs.ToArray())
        {
            if (!IsInstanceValid(mob)) continue;
            mob.Tick(delta, Player.Center, false, World.Grid, (_, _) => { });
        }
    }

    // -------------------------------------------------------------- save/load

    /// <summary>Simpan otomatis; true kalau berhasil (server atau penyimpanan lokal).</summary>
    public bool Autosave(string reason)
    {
        if (ProfileId is null || Api is null || Player is null) return false;
        var info = Api.Save(ProfileId, CurrentSlot, BuildState());
        var target = Api.Offline ? "lokal" : $"slot {CurrentSlot}";
        if (info is not null) Hud?.Toast($"Tersimpan ({reason}) · {target}");
        else Hud?.Toast($"Gagal menyimpan ({reason}): {Api.LastError}");
        return info is not null;
    }

    /// <summary>Simpan sebelum game ditutup (tombol Keluar maupun jendela ditutup).</summary>
    public bool SaveOnExit()
    {
        if (ProfileId is null) return false;
        AutosaveTimer = 0;
        return Autosave("keluar");
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMCloseRequest)
        {
            SaveOnExit();
            Sfx?.StopAll();
            GetTree().Quit(0);
        }
    }

    public override void _ExitTree() => Sfx?.StopAll();

    public GameState BuildState()
    {
        var state = Player!.State;
        state.X = Player.Position.X;
        state.Y = Player.Position.Y;
        state.Zone = ZoneId;
        state.Playtime = Playtime;
        return state;
    }

    public void SaveToSlot(int slot)
    {
        if (ProfileId is null || Api is null || Player is null) return;
        CurrentSlot = slot;
        var info = Api.Save(ProfileId, slot, BuildState());
        Hud?.Toast(info is null
            ? $"Gagal menyimpan slot {slot}: {Api.LastError}"
            : $"Perjalanan tersimpan di slot {slot}");
        if (info is not null) Hud?.SetJournal(Api.Journey(ProfileId));
    }

    public void LoadFromSlot(int slot)
    {
        if (ProfileId is null || Api is null) return;
        var state = Api.Load(ProfileId, slot);
        if (state is null)
        {
            Hud?.Toast($"Slot {slot} kosong.");
            return;
        }
        ApplyLoadedState(state);
        Hud?.Toast($"Lanjut dari slot {slot} — {state.Zone}");
        CloseMenus();
    }

    private void ApplyLoadedState(GameState state)
    {
        if (Player is null || World is null) return;
        Player.State.Hp = state.Hp;
        Player.State.MaxHp = state.MaxHp;
        Player.State.Level = state.Level;
        Player.State.Xp = state.Xp;
        Player.State.SkillLevel = state.SkillLevel;
        Player.State.Skills = state.Skills;
        Player.State.Zone = state.Zone;
        Player.State.Checkpoint = state.Checkpoint;
        Player.State.Flags = state.Flags;
        Player.State.Defeated = state.Defeated;
        Player.State.Gold = state.Gold;
        Player.State.Potions = state.Potions;
        Player.State.Deaths = state.Deaths;
        Player.State.Kills = state.Kills;
        Playtime = state.Playtime;
        Player.ApplyState(state);
        CloseDialog();
        // tampilkan peti yang sudah dibuka sesuai flag tersimpan
        foreach (var chest in World.Data.Chests)
        {
            if (ChestOpened(chest)) World.SetPropTile(chest.Col, chest.Row, "chest_open");
        }
        World.ZoneAt(Player.Position);
        ZoneId = state.Zone;
        ZoneName = World.Data.Zones.FirstOrDefault(z => z.Id == state.Zone)?.Name ?? state.Zone;
    }

    // ------------------------------------------------------------------ menu

    private void ShowProfiles()
    {
        if (Hud is null || Api is null) return;
        Hud.ShowPauseMenu(false);
        Hud.ShowProfileMenu(true);
        var profiles = Api.ListProfiles();
        var status = profiles.Count > 0
            ? "Pilih profil di bawah, atau buat yang baru:"
            : "Belum ada profil. Tulis nama lalu tekan \"Buat profil\".";
        Hud.SetProfiles(profiles, status);
    }

    private void CreateProfile(string name)
    {
        if (Api is null || Hud is null) return;
        var profile = Api.CreateProfile(name);
        if (profile is null)
        {
            Hud.Toast($"Gagal membuat profil: {Api.LastError}");
            ShowProfiles();
            return;
        }
        StartProfile(profile.Id);
    }

    private void StartProfile(string profileId)
    {
        if (Api is null || Hud is null) return;
        ProfileId = profileId;
        var profiles = Api.ListProfiles();
        ProfileName = profiles.FirstOrDefault(p => p.Id == profileId)?.Name ?? profileId;
        Hud.ShowProfileMenu(false);
        Hud.SetJournal(Api.Journey(profileId));

        var resume = Api.Resume(profileId);
        if (resume is { } saved)
        {
            CurrentSlot = saved.Slot;
            ApplyLoadedState(saved.State);
            Hud.Toast($"Lanjut perjalanan {ProfileName} — slot {saved.Slot}");
        }
        else
        {
            Hud.Toast($"Petualangan baru dimulai, {ProfileName}. Tekan F untuk menyimpan.");
        }
    }

    public void OpenPauseMenu()
    {
        if (Hud is null || Player is null) return;
        var time = TimeSpan.FromSeconds(Playtime);
        Hud.SetPauseSummary($"{ProfileName} · Lv {Player.State.Level} · {Player.State.Gold} gold · " +
            $"bunuh {Player.State.Kills} · tumbang {Player.State.Deaths} · {time.Minutes:00}:{time.Seconds:00} · {ZoneName}");
        Hud.ShowPauseMenu(true);
        if (ProfileId is not null && Api is not null) Hud.SetJournal(Api.Journey(ProfileId));
    }

    /// <summary>
    /// Tracker quest di HUD (tahap 2g, murni informasi): quest yang belum
    /// selesai dihitung dari state pemain. Dipanggil tiap frame tetapi hanya
    /// membangun ulang teks kalau jumlah kill/flag berubah (cache signature).
    /// </summary>
    public void RefreshQuestTracker()
    {
        if (Player is null || World is null || Hud is null) return;
        var defeatedTotal = 0;
        foreach (var pair in Player.State.Defeated) defeatedTotal += pair.Value;
        var signature = Player.State.Kills * 7 + Player.State.Flags.Count * 131 + defeatedTotal * 17;
        if (signature == _questTrackerSignature) return;
        _questTrackerSignature = signature;

        var lines = new List<string>();
        foreach (var npc in World.Data.Npcs)
        {
            var quest = npc.Quest;
            if (quest is null || quest.Flag.Length == 0) continue;
            if (Player.State.Flags.GetValueOrDefault(quest.Flag)) continue;
            var killed = Math.Min(Player.State.Defeated.GetValueOrDefault(quest.Target), quest.Count);
            lines.Add(killed >= quest.Count
                ? $"- {npc.Name}: selesai, temui dia"
                : $"- {npc.Name}: kalahkan {quest.Target} {killed}/{quest.Count}");
        }
        Hud.SetQuestTracker(lines.Count == 0 ? "" : "QUEST\n" + string.Join("\n", lines));
    }

    public void CloseMenus()
    {
        Hud?.ShowPauseMenu(false);
        Hud?.ShowProfileMenu(false);
    }
}
