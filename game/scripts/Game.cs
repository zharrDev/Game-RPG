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
    public EventBus Bus { get; private set; } = null!;
    public ProgressSystem Progress { get; private set; } = null!;
    public SpawnSystem Spawn { get; private set; } = null!;
    public CombatSystem Combat { get; private set; } = null!;
    public DialogSystem Dialog { get; private set; } = null!;
    public InteractionSystem InteractSystem { get; private set; } = null!;
    public WorldManager Worlds { get; private set; } = null!;
    public ChunkSystem Chunks { get; private set; } = null!;
    public FogOfWar Fog { get; private set; } = null!;
    public WeatherSystem Weather { get; private set; } = null!;
    public FireSystem Fire { get; private set; } = null!;
    public FireVisuals FireVisuals { get; private set; } = null!;

    /// <summary>Kotak dialog sedang terbuka (permainan dijeda).</summary>
    public bool DialogOpen => Dialog.Open;
    public string DialogLine => Dialog.Line;
    public string? TalkingTo => Dialog.TalkingTo;
    public string? ProfileId;
    public string ProfileName = "";
    public int CurrentSlot = 1;
    public string ZoneId = "";
    public string ZoneName = "";
    public double AutosaveTimer;
    public bool TestMode;

    private readonly CameraRig _cameraRig = new();
    private readonly TimeSystem _time = new();
    private UiManager _ui = null!;
    private SaveSystem _save = null!;
    private bool _testPending;
    private double _stepTimer;
    private double _extinguishCooldown;
    private bool _airborne;
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
        _save = new SaveSystem(this);
        Bus = new EventBus();
        Progress = new ProgressSystem(this);
        Spawn = new SpawnSystem(this);
        Combat = new CombatSystem(this);
        Dialog = new DialogSystem(this);
        InteractSystem = new InteractionSystem(this);
        Worlds = new WorldManager(this);
        Chunks = new ChunkSystem();
        Weather = new WeatherSystem();
        Weather.SetStage(0);
        BuildWorld();

        // fog of war dunia: chunk belum dijelajahi digelapkan (di atas dunia, di bawah HUD)
        Fog = new FogOfWar();
        World.Root.AddChild(Fog);
        Fog.Configure(Chunks);

        // api gameplay: bahan bakar/kelembapan dari terrain, angin & hujan dari cuaca
        Fire = new FireSystem();
        Fire.Configure(this);

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

        // nyala api gameplay mengikuti sel api (bara memakai kolam Ambience)
        FireVisuals = new FireVisuals();
        World.Root.AddChild(FireVisuals);
        FireVisuals.Configure(this);

        Api = new SaveApi(_serverUrl);
        var version = "";
        var backendUp = Api.Healthy(out version);
        if (!backendUp && !TestMode)
            GD.PrintErr($"[game] backend {_serverUrl} tidak bisa dihubungi: {Api.LastError}");
        else if (backendUp)
            GD.Print($"[game] backend v{version} di {_serverUrl}");

        Hud = new Hud();
        AddChild(Hud);
        _ui = new UiManager(this);
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

        // umpan balik damage: sistem mengirim event, efek visual/audio dipasang di sini
        Bus.PlayerDamaged += OnPlayerDamagedFeedback;

        if (World is not null)
        {
            Hud.SetMinimap(World.Minimap);
            Hud.ConfigureMinimapFog(Chunks, World.Data.PixelSize);
        }

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
        Register("extinguish", Key.R);
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
        Player.LeveledUp += Progress.OnLevelUp;
        World.Root.AddChild(Player);

        _cameraRig.Build(Player, data.PixelSize);

        // game feel (tahap 2f): shake kamera, hit-stop, debu langkah, percikan
        Feel = new GameFeel();
        Feel.Configure();
        World.Root.AddChild(Feel);

        Spawn.SpawnMobs();
        Spawn.SpawnNpcs();
        UpdateZone(true);

        // chunk: aktivasi aktor + fog of war (peta 96x72 -> 12x9 chunk 8 tile)
        Chunks.Configure(data.Width, data.Height);
        Chunks.Update(Player.Position);
    }

    private void AttachFireFx() => Worlds.AttachFireFx();

    /// <summary>True kalau NPC ini punya quest yang belum selesai (penanda "!").</summary>
    public bool QuestPending(NpcInfo info) => Progress.QuestPending(info);

    private void RefreshNpcMarkers() => Spawn.RefreshNpcMarkers();

    /// <summary>Catat peristiwa ke catatan perjalanan (dilakukan server, bukan client).</summary>
    public void LogEvent(string kind, string detail) => _save.LogEvent(kind, detail);

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
        _extinguishCooldown = Math.Max(0, _extinguishCooldown - delta);
        // asap yang menempel di dunia: ikut titik tengah kamera (termasuk smoothing)
        var camCenter = _cameraRig.ScreenCenter;
        Fx?.SetCameraCenter(camCenter);
        // jam dunia + denyut visual lingkungan (kedip obor, riak air)
        _time.Advance(delta, World);
        // ambience api: nyala, bara, abu zona terbakar, tunas (murni visual)
        Ambience?.SetCameraCenter(camCenter);
        Ambience?.Advance(delta);
        if (Fx is not null && Ambience is not null)
            Fx.AshDensity = _ashBase + Ambience.AshBoost + (Fire?.SmokeAt(camCenter) ?? 0f) * 0.3f;

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
        if (playing && Player.Alive && !frozen && Input.IsActionJustPressed("extinguish")) TryExtinguish();
        if (playing && Player.Alive && !frozen)
        {
            Weather.Advance(delta);
            Fire.Advance(delta);
            Player.SmokeExposure = Fire.SmokeAt(Player.Position);
            Player.Tick(delta, ReadInput(), World.Grid);
            Fire.TickPlayer(delta);
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
            Player.SmokeExposure = 0;
            Player.Tick(delta, default, World.Grid);
        }

        if (!frozen)
        {
            Combat.TickMobs(delta, playing);
        }

        // warga mondar-mandir singkat (tahap 2f, murni visual); berhenti saat dialog/menu/hit-stop
        Spawn.TickNpcs(delta, !playing || frozen || DialogOpen || !Player.Alive);

        if (playing && !Player.Alive && Player.DeathTimer >= Rules.DeathDuration)
            Combat.Respawn();

        // chunk di sekitar pemain (aktivasi aktor + reveal fog)
        Chunks.Update(Player.Position);

        // HUD selalu hidup (jam, minimap, tracker) — termasuk saat dialog/menu
        _ui.UpdateHud();
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

    private void UpdateZone(bool force) => Worlds.UpdateZone(force);

    private void UpdateCheckpoint() => Worlds.UpdateCheckpoint();

    // ------------------------------------------------------- interaksi (facade)

    /// <summary>Aksi tombol E: bicara dengan NPC terdekat; kalau tidak ada, istirahat di checkpoint.</summary>
    public bool Interact() => InteractSystem.Interact();

    public ChestInfo? ChestNear(Vector2 position) => InteractSystem.ChestNear(position);

    public bool ChestOpened(ChestInfo chest) => InteractSystem.ChestOpened(chest);

    /// <summary>Buka peti: gold + 1 potion, flag tersimpan supaya tidak bisa dicuri dua kali.</summary>
    public bool OpenChest(ChestInfo chest) => InteractSystem.OpenChest(chest);

    /// <summary>Minum potion (tombol Q).</summary>
    public bool DrinkPotion() => InteractSystem.DrinkPotion();

    /// <summary>Beli potion di pasar (tombol di menu pause).</summary>
    public bool BuyPotion() => InteractSystem.BuyPotion();

    public void OpenDialog(NpcInfo info) => Dialog.OpenWith(info);

    public void AdvanceDialog() => Dialog.Advance();

    public void CloseDialog() => Dialog.Close();

    /// <summary>Tombol E di checkpoint: istirahat memulihkan HP & energi, lalu simpan.</summary>
    public bool RestAtCheckpoint() => InteractSystem.RestAtCheckpoint();

    // ------------------------------------------------------------- tempur (facade)

    /// <summary>Selesaikan serangan pemain (dipakai Player.Strike & tes).</summary>
    public void ApplyStrike(Vector2 origin, string facing, int damage, string kind) =>
        Combat.ApplyStrike(origin, facing, damage, kind);

    /// <summary>Tick semua musuh (dipakai tes headless supaya timer kematian berjalan).</summary>
    public void MobsTickForTest(double delta) => Combat.MobsTickForTest(delta);

    /// <summary>Semprot air (R): padamkan api di sekitar pemain dengan biaya napas.</summary>
    public bool TryExtinguish()
    {
        if (Player is null || !Player.Alive || Fire is null) return false;
        if (_extinguishCooldown > 0) return false;
        if (Player.Energy < Rules.ExtinguishCost)
        {
            Hud?.Toast("Napas habis — berhenti sebentar untuk mengisi.");
            return false;
        }
        var removed = Fire.Extinguish(Player.Position, (float)Rules.ExtinguishRange);
        if (removed == 0)
        {
            Hud?.Toast("Tidak ada api di dekat sini.");
            return false;
        }
        Player.Energy = Math.Max(0, Player.Energy - Rules.ExtinguishCost);
        _extinguishCooldown = Rules.ExtinguishCooldown;
        Sfx?.Play("step", -4f);
        Feel?.Burst(Player.Position, new Color(0.52f, 0.82f, 1f, 0.95f), 10);
        Hud?.Toast($"Api padam · {removed} titik");
        return true;
    }

    /// <summary>Damage dari bahaya lingkungan (api/asap): tanpa musuh, tetap umpan balik penuh.</summary>
    public void DamageFromHazard(int damage, string cause)
    {
        if (Player is null || !Player.Alive) return;
        var died = Player.TakeDamage(damage);
        if (died)
        {
            Player.State.Deaths++;
            LogEvent("death", $"{cause} di {ZoneName}");
        }
        Sfx?.Play(died ? "death" : "hurt");
        Feel?.Shake(died ? 5.5f : 3.2f);
        Feel?.HitStop(0.06);
        Hud?.FlashDamage();
        Hud?.Toast($"{(cause.Length > 0 ? char.ToUpper(cause[0]) + cause[1..] : cause)} · -{damage} HP");
    }

    /// <summary>Umpan balik saat pemain terluka: audio, shake, hit-stop, flash HUD.</summary>
    private void OnPlayerDamagedFeedback(Mob mob, int damage, bool died)
    {
        Sfx?.Play(died ? "death" : "hurt");
        Feel?.Shake(died ? 5.5f : 4f);
        Feel?.HitStop(0.085);
        if (Player is not null) Feel?.Burst(Player.Center, new Color(1f, 0.42f, 0.36f, 0.95f), 6);
        Hud?.FlashDamage();
        Hud?.Toast($"{mob.Stats.Display} menyerangmu  ·  -{damage} HP");
    }

    // -------------------------------------------------------------- save/load

    /// <summary>Simpan otomatis; true kalau berhasil (server atau penyimpanan lokal).</summary>
    public bool Autosave(string reason) => _save.Autosave(reason);

    /// <summary>Simpan sebelum game ditutup (tombol Keluar maupun jendela ditutup).</summary>
    public bool SaveOnExit() => _save.SaveOnExit();

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

    public GameState BuildState() => _save.BuildState();

    public void SaveToSlot(int slot) => _save.SaveToSlot(slot);

    public void LoadFromSlot(int slot) => _save.LoadFromSlot(slot);

    // ------------------------------------------------------------------ menu

    private void ShowProfiles() => _save.ShowProfiles();

    private void CreateProfile(string name) => _save.CreateProfile(name);

    private void StartProfile(string profileId) => _save.StartProfile(profileId);

    // ---------------------------------------------------------------- UI/menu

    public void OpenPauseMenu() => _ui.OpenPauseMenu();

    public void RefreshQuestTracker() => _ui.RefreshQuestTracker();

    public void CloseMenus() => _ui.CloseMenus();
}
