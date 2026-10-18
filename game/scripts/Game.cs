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

    private Camera2D _camera = null!;
    private bool _menuVisible;
    private bool _testPending;
    private double _waterTimer;
    private double _stepTimer;

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
            else if (arg == "--test") TestMode = true;
        }

        Hud.EnsurePixelFont();   // sebelum BuildWorld: NPC memakai font pixel untuk penanda "!"
        SetupInput();
        BuildWorld();

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

        if (TestMode)
        {
            _testPending = true;
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

        _camera = new Camera2D
        {
            Name = "Camera",
            PositionSmoothingEnabled = true,
            PositionSmoothingSpeed = 8,
            LimitLeft = 0,
            LimitTop = 0,
            LimitRight = (int)data.PixelSize.X,
            LimitBottom = (int)data.PixelSize.Y,
            Position = new Vector2(0, -12),
        };
        Player.AddChild(_camera);
        _camera.MakeCurrent();

        SpawnMobs();
        SpawnNpcs();
        UpdateZone(true);
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
        if (World is null || Player is null || Hud is null) return;

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
        if (playing && Player.Alive)
        {
            Player.Tick(delta, ReadInput(), World.Grid);
            if (Player.Action is "walk" or "run")
            {
                _stepTimer += delta;
                if (_stepTimer >= (Player.Action == "run" ? 0.24 : 0.34))
                {
                    _stepTimer = 0;
                    Sfx?.Play("step", -20f);
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
        else if (playing)
        {
            Player.Tick(delta, default, World.Grid);
        }

        foreach (var mob in Mobs.ToArray())
        {
            if (!IsInstanceValid(mob)) continue;
            mob.Tick(delta, Player.Center, Player.Alive && playing, World.Grid, OnMobHitsPlayer);
        }

        if (playing && !Player.Alive && Player.DeathTimer >= Rules.DeathDuration)
            Respawn();

        // obor & lentera berkedip hangat (ambience desa/reruntuhan)
        if (World.FlickerProps.Count > 0)
        {
            var seconds = (float)Time.GetTicksMsec() / 1000f;
            foreach (var (sprite, phase) in World.FlickerProps)
            {
                var pulse = 0.82f + 0.18f * MathF.Sin(seconds * 6.5f + phase) * MathF.Sin(seconds * 2.7f + phase * 1.7f);
                sprite.Modulate = new Color(pulse, pulse * 0.94f, pulse * 0.78f, 1f);
            }
        }

        // riak air: tukar fase tile tiap 0.6 detik (shoreline & deep water)
        _waterTimer += delta;
        if (_waterTimer >= 0.6)
        {
            _waterTimer = 0;
            World.SetWaterPhase(World.WaterPhase + 1);
        }

        Hud.UpdateStats(Player.State, Player.Energy, Playtime, ZoneName, Player.State.Checkpoint);
        Hud.UpdateMinimap(Player.Position, World.Data.PixelSize);
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
        foreach (var mob in Mobs.ToArray())
        {
            if (mob.Dead || !IsInstanceValid(mob)) continue;
            var offset = mob.Position - origin;
            var distance = offset.Length();
            if (distance > range) continue;
            if (kind != "skill" && distance > 0.5f && dir.Dot(offset.Normalized()) < Rules.AttackArcDot) continue;
            hit++;
            var died = mob.TakeDamage(damage);
            var label = kind == "skill" ? "Aura Slash" : "Serangan";
            Hud?.Toast($"{label}: {mob.Stats.Display} -{damage} HP" + (died ? " (tumbang)" : ""));
        }
        if (hit > 0) Sfx?.Play("hit");
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

    private void OpenPauseMenu()
    {
        if (Hud is null) return;
        Hud.ShowPauseMenu(true);
        if (ProfileId is not null && Api is not null) Hud.SetJournal(Api.Journey(ProfileId));
    }

    private void CloseMenus()
    {
        Hud?.ShowPauseMenu(false);
        Hud?.ShowProfileMenu(false);
    }
}
