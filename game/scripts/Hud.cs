using System;
using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>HUD + menu (pause, profil) — semua dibangun dari kode, viewport 480x270.</summary>
public sealed partial class Hud : CanvasLayer
{
    private ColorRect _hpBar = null!, _energyBar = null!, _xpBar = null!;
    private Label _hpText = null!, _statsText = null!, _zoneText = null!, _toast = null!, _hint = null!;
    private Label _clock = null!, _questText = null!;
    private TextureRect _vignette = null!;
    private Panel _pausePanel = null!, _profilePanel = null!;
    private VBoxContainer _pauseBox = null!, _profileBox = null!;
    private Label _journalText = null!, _profileStatus = null!, _pauseSummary = null!;
    private Panel _dialogPanel = null!;
    private Label _dialogSpeaker = null!, _dialogText = null!;
    private TextureRect _minimap = null!;
    private ColorRect _minimapDot = null!;
    private LineEdit _nameInput = null!;
    private double _toastTimer;

    public event Action<int>? SaveSlot;
    public event Action<int>? LoadSlot;
    public event Action? CloseMenu;
    public event Action? QuitToDesktop;
    public event Action? BackToProfiles;
    public event Action<string>? ProfilePicked;
    public event Action<string>? ProfileCreated;
    public event Action? BuyPotion;

    public bool MenuOpen => _pausePanel.Visible;
    public bool ProfileMenuOpen => _profilePanel.Visible;
    public bool DialogOpen => _dialogPanel.Visible;
    public string DialogText => _dialogText.Text;
    public string DialogSpeaker => _dialogSpeaker.Text;
    public bool MinimapVisible => _minimap.Texture is not null;
    public string ClockText => _clock.Text;
    public string QuestTrackerText => _questText.Text;
    public string PauseSummaryText => _pauseSummary.Text;
    public float VignetteAlpha { get; private set; }
    public double DamageFlashRemaining { get; private set; }
    public float HpRatio { get; private set; } = 1f;

    /// <summary>Font bitmap 5x7 (tools/generate_pixel_font.py). Null = pakai font default.</summary>
    public static FontFile? PixelFont { get; private set; }
    private const int PixelFontSize = 8;

    public Hud()
    {
        Name = "Hud";
        Layer = 10;
        EnsurePixelFont();

        // vignette (tahap 2g): di bawah semua panel HUD, di atas dunia.
        // Putih + alpha; di-modulate merah saat terluka / HP rendah.
        _vignette = new TextureRect
        {
            Name = "Vignette",
            Position = Vector2.Zero,
            Size = new Vector2(480, 270),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        try
        {
            _vignette.Texture = Assets.LoadTexture(Assets.FxVignettePng);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[hud] vignette gagal dimuat: {ex.Message}");
        }
        AddChild(_vignette);

        var top = new Panel { Position = new Vector2(4, 4), Size = new Vector2(196, 40) };
        AddChild(top);
        _hpText = MakeLabel(new Vector2(8, 6), 10);
        top.AddChild(_hpText);
        _hpBar = MakeBar(new Vector2(8, 19), new Vector2(180, 5), new Color(0.65f, 0.22f, 0.25f));
        top.AddChild(_hpBar);
        _energyBar = MakeBar(new Vector2(8, 26), new Vector2(180, 4), new Color(0.28f, 0.6f, 0.85f));
        top.AddChild(_energyBar);
        _xpBar = MakeBar(new Vector2(8, 32), new Vector2(180, 4), new Color(0.85f, 0.75f, 0.3f));
        top.AddChild(_xpBar);

        _statsText = MakeLabel(new Vector2(206, 6), 10);
        AddChild(_statsText);
        _zoneText = MakeLabel(new Vector2(206, 20), 10);
        AddChild(_zoneText);

        _clock = MakeLabel(new Vector2(206, 34), 8);
        _clock.Modulate = new Color(0.95f, 0.88f, 0.6f);
        AddChild(_clock);

        _questText = MakeLabel(new Vector2(206, 46), 8);
        _questText.Modulate = new Color(0.85f, 0.92f, 1f);
        _questText.Visible = false;
        AddChild(_questText);

        _toast = MakeLabel(new Vector2(8, 48), 10);
        _toast.Modulate = new Color(1f, 0.9f, 0.5f);
        AddChild(_toast);

        _hint = MakeLabel(new Vector2(8, 252), 9);
        _hint.Modulate = new Color(0.85f, 0.85f, 0.9f, 0.85f);
        _hint.Text = "WASD gerak · Shift lari · J serang · K skill · Spasi lompat · E bicara/istirahat/peti · Q potion · F simpan · Esc menu";
        AddChild(_hint);

        BuildPauseMenu();
        BuildProfileMenu();
        BuildDialog();
        BuildMinimap();
    }

    /// <summary>Peta mini di kanan bawah (1 px per tile) + titik pemain.</summary>
    private void BuildMinimap()
    {
        _minimap = new TextureRect
        {
            Name = "Minimap",
            Position = new Vector2(396, 186),
            Size = new Vector2(72, 54),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_minimap);

        _minimapDot = new ColorRect
        {
            Name = "PlayerDot",
            Position = new Vector2(0, 0),
            Size = new Vector2(3, 3),
            Color = new Color(1f, 0.93f, 0.42f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _minimap.AddChild(_minimapDot);
    }

    public void SetMinimap(Texture2D texture) => _minimap.Texture = texture;

    public void UpdateMinimap(Vector2 playerPosition, Vector2 worldSize)
    {
        if (_minimap.Texture is null || worldSize.X <= 0 || worldSize.Y <= 0) return;
        var factor = new Vector2(_minimap.Size.X / worldSize.X, _minimap.Size.Y / worldSize.Y);
        _minimapDot.Position = new Vector2(
            Math.Clamp(playerPosition.X * factor.X - 1.5f, 0, _minimap.Size.X - 3),
            Math.Clamp(playerPosition.Y * factor.Y - 1.5f, 0, _minimap.Size.Y - 3));
    }

    /// <summary>Kotak dialog di bawah layar (gaya JRPG): nama pembicara + teks.</summary>
    private void BuildDialog()
    {
        _dialogPanel = new Panel { Position = new Vector2(16, 174), Size = new Vector2(448, 82), Visible = false };
        AddChild(_dialogPanel);

        _dialogSpeaker = MakeLabel(new Vector2(10, 6), 9);
        _dialogSpeaker.Modulate = new Color(1f, 0.87f, 0.5f);
        _dialogPanel.AddChild(_dialogSpeaker);

        _dialogText = MakeLabel(new Vector2(10, 22), 9);
        _dialogText.Size = new Vector2(428, 42);
        _dialogText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dialogPanel.AddChild(_dialogText);

        var hint = MakeLabel(new Vector2(372, 64), 8);
        hint.Text = "E lanjut";
        hint.Modulate = new Color(0.75f, 0.82f, 0.95f, 0.9f);
        _dialogPanel.AddChild(hint);
    }

    public void ShowDialog(string speaker, string text)
    {
        _dialogSpeaker.Text = speaker;
        _dialogText.Text = text;
        _dialogPanel.Visible = true;
    }

    public void HideDialog() => _dialogPanel.Visible = false;

    /// <summary>
    /// Muat font pixel sekali. Dipanggil Game sebelum dunia dibangun supaya label
    /// yang dibuat lebih awal (mis. penanda quest NPC) juga memakai font pixel.
    /// Kalau gagal, HUD tetap jalan dengan font default.
    /// </summary>
    public static void EnsurePixelFont()
    {
        if (PixelFont is not null) return;
        var font = new FontFile();
        var error = font.LoadBitmapFont(Assets.PixelFont);
        if (error != Error.Ok)
        {
            GD.PrintErr($"[hud] font pixel gagal dimuat ({error}) — pakai font default");
            return;
        }
        font.FixedSize = PixelFontSize;
        PixelFont = font;
    }

    private static Label MakeLabel(Vector2 pos, int size)
    {
        var label = new Label { Position = pos };
        // font bitmap: ukuran 8 = 1:1 piksel, judul 16 = kelipatan 2 (tetap tajam)
        var fontSize = PixelFont is null ? size : (size <= 10 ? PixelFontSize : PixelFontSize * 2);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        if (PixelFont is not null) label.AddThemeFontOverride("font", PixelFont);
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        label.AddThemeConstantOverride("outline_size", PixelFont is null ? 4 : 2);
        return label;
    }

    private static ColorRect MakeBar(Vector2 pos, Vector2 size, Color color) =>
        new() { Position = pos, Size = size, Color = color };

    private void BuildPauseMenu()
    {
        _pausePanel = new Panel { Position = new Vector2(108, 24), Size = new Vector2(264, 222), Visible = false };
        AddChild(_pausePanel);
        _pauseBox = new VBoxContainer { Position = new Vector2(12, 10), Size = new Vector2(240, 200) };
        _pauseBox.AddThemeConstantOverride("separation", 3);
        _pausePanel.AddChild(_pauseBox);

        var title = MakeLabel(Vector2.Zero, 11);
        title.Text = "MENU — jeda permainan";
        _pauseBox.AddChild(title);

        for (var slot = 1; slot <= Rules.SlotCount; slot++)
        {
            var index = slot;
            var row = new HBoxContainer();
            var save = MakeButton($"Simpan slot {index}", 118);
            save.Pressed += () => SaveSlot?.Invoke(index);
            var load = MakeButton($"Muat slot {index}", 118);
            load.Pressed += () => LoadSlot?.Invoke(index);
            row.AddChild(save);
            row.AddChild(load);
            _pauseBox.AddChild(row);
        }

        var buy = MakeButton($"Beli potion — {Rules.PotionPrice} gold", 240);
        buy.Pressed += () => BuyPotion?.Invoke();
        _pauseBox.AddChild(buy);

        var journal = MakeLabel(Vector2.Zero, 9);
        journal.Text = "Catatan perjalanan:";
        _pauseBox.AddChild(journal);
        _journalText = MakeLabel(Vector2.Zero, 9);
        _journalText.Modulate = new Color(0.85f, 0.9f, 1f);
        _pauseBox.AddChild(_journalText);

        _pauseSummary = MakeLabel(Vector2.Zero, 9);
        _pauseSummary.Modulate = new Color(0.75f, 0.82f, 0.72f);
        _pauseBox.AddChild(_pauseSummary);

        var back = MakeButton("Lanjut bermain", 240);
        back.Pressed += () => CloseMenu?.Invoke();
        _pauseBox.AddChild(back);
        var profiles = MakeButton("Ganti profil", 240);
        profiles.Pressed += () => BackToProfiles?.Invoke();
        _pauseBox.AddChild(profiles);
        var quit = MakeButton("Keluar", 240);
        quit.Pressed += () => QuitToDesktop?.Invoke();
        _pauseBox.AddChild(quit);
    }

    private void BuildProfileMenu()
    {
        _profilePanel = new Panel { Position = new Vector2(70, 30), Size = new Vector2(340, 210), Visible = false };
        AddChild(_profilePanel);
        _profileBox = new VBoxContainer { Position = new Vector2(12, 8), Size = new Vector2(316, 196) };
        _profileBox.AddThemeConstantOverride("separation", 2);
        _profilePanel.AddChild(_profileBox);

        var title = MakeLabel(Vector2.Zero, 12);
        title.Text = "ARGA — Open World RPG";
        _profileBox.AddChild(title);
        var sub = MakeLabel(Vector2.Zero, 9);
        sub.Text = "Pilih profil untuk menyimpan perjalananmu (server C# + SQLite).";
        _profileBox.AddChild(sub);

        var row = new HBoxContainer();
        _nameInput = new LineEdit { Size = new Vector2(190, 20), PlaceholderText = "nama pemain baru" };
        _nameInput.AddThemeFontSizeOverride("font_size", PixelFont is null ? 10 : PixelFontSize);
        if (PixelFont is not null) _nameInput.AddThemeFontOverride("font", PixelFont);
        var create = MakeButton("Buat profil", 110);
        create.Pressed += () =>
        {
            var name = _nameInput.Text.Trim();
            if (name.Length > 0) ProfileCreated?.Invoke(name);
        };
        row.AddChild(_nameInput);
        row.AddChild(create);
        _profileBox.AddChild(row);

        _profileStatus = MakeLabel(Vector2.Zero, 9);
        _profileStatus.Modulate = new Color(1f, 0.85f, 0.6f);
        _profileBox.AddChild(_profileStatus);
    }

    private static Button MakeButton(string text, float width)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(width, 18) };
        button.AddThemeFontSizeOverride("font_size", PixelFont is null ? 9 : PixelFontSize);
        if (PixelFont is not null) button.AddThemeFontOverride("font", PixelFont);
        return button;
    }

    public void SetProfiles(List<ProfileInfo> profiles, string status)
    {
        foreach (var child in _profileBox.GetChildren())
        {
            if (child is HBoxContainer container && container.HasMeta("profile_row"))
                container.QueueFree();
        }
        _profileStatus.Text = status;
        foreach (var profile in profiles)
        {
            var row = new HBoxContainer();
            row.SetMeta("profile_row", true);
            var pick = MakeButton($"{profile.Name}  ({profile.Id})", 250);
            var id = profile.Id;
            pick.Pressed += () => ProfilePicked?.Invoke(id);
            row.AddChild(pick);
            _profileBox.AddChild(row);
        }
    }

    public void ShowProfileMenu(bool visible)
    {
        _profilePanel.Visible = visible;
    }

    public void ShowPauseMenu(bool visible)
    {
        _pausePanel.Visible = visible;
    }

    public void SetJournal(List<JournalEntry> entries)
    {
        if (entries.Count == 0)
        {
            _journalText.Text = "  (belum ada catatan — simpan dulu)";
            return;
        }
        var lines = new List<string>();
        for (var i = 0; i < Math.Min(4, entries.Count); i++)
        {
            var entry = entries[i];
            var stamp = entry.CreatedAt.Length >= 16 ? entry.CreatedAt[11..16] : entry.CreatedAt;
            lines.Add($"  · {stamp} {entry.Kind}: {Truncate(entry.Detail, 40)}");
        }
        _journalText.Text = string.Join("\n", lines);
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? "-" : (value.Length <= max ? value : value[..max]);

    public void Toast(string message)
    {
        _toast.Text = message;
        _toastTimer = 3.5;
    }

    /// <summary>Jam dunia di HUD (tahap 2g): "Senja 17:24" dari Lighting.TimeOfDay.</summary>
    public void UpdateClock(float timeOfDay)
    {
        var hours = ((timeOfDay % 1f) + 1f) % 1f * 24f;
        var hh = (int)hours;
        var mm = (int)((hours - hh) * 60f);
        var phase = hh is >= 5 and < 11 ? "Pagi" : hh is >= 11 and < 16 ? "Siang" : hh is >= 16 and < 19 ? "Senja" : "Malam";
        var text = $"{phase} {hh:00}:{mm:00}";
        if (_clock.Text != text) _clock.Text = text;
    }

    /// <summary>Tracker quest di HUD (tahap 2g); teks kosong = disembunyikan.</summary>
    public void SetQuestTracker(string text)
    {
        if (_questText.Text == text) return;
        _questText.Text = text;
        _questText.Visible = text.Length > 0;
    }

    /// <summary>Ringkasan profil di menu jeda (tahap 2g).</summary>
    public void SetPauseSummary(string text) => _pauseSummary.Text = text;

    /// <summary>Kedip merah singkat saat pemain terluka (vignette).</summary>
    public void FlashDamage() => DamageFlashRemaining = 1.0;

    /// <summary>
    /// Majukan overlay vignette: kedip terluka mereda + denyut lembut saat HP
    /// rendah (&lt; 30%). Dipanggil tiap frame dari _Process; tes memanggil
    /// langsung supaya deterministik.
    /// </summary>
    public void AdvanceOverlay(double delta, float seconds)
    {
        DamageFlashRemaining = Math.Max(0, DamageFlashRemaining - delta * 1.6);
        var lowHp = HpRatio < 0.30f ? 0.20f + 0.08f * (0.5f + 0.5f * MathF.Sin(seconds * 5.5f)) : 0f;
        VignetteAlpha = MathF.Max(lowHp, (float)DamageFlashRemaining * 0.55f);
        _vignette.Modulate = new Color(1f, 0.32f, 0.32f, VignetteAlpha);
    }

    public void SetHint(string message) => _hint.Text = message;

    public void UpdateStats(GameState state, double energy, double playtime, string zoneName, string checkpointName)
    {
        HpRatio = state.MaxHp > 0 ? (float)state.Hp / state.MaxHp : 1f;
        _hpText.Text = $"HP {state.Hp}/{state.MaxHp}   Lv {state.Level}   Skill Lv {state.SkillLevel}";
        _hpBar.Size = new Vector2(Math.Max(0, 180f * state.Hp / Math.Max(1, state.MaxHp)), 5);
        _energyBar.Size = new Vector2((float)Math.Max(0, 180f * energy / Rules.MaxEnergy), 4);
        var need = Rules.XpToNext(state.Level);
        _xpBar.Size = new Vector2((float)Math.Max(0, 180f * state.Xp / Math.Max(1, need)), 4);

        var time = TimeSpan.FromSeconds(playtime);
        _statsText.Text = $"{state.Xp}/{need} XP   ·   {time.Minutes:00}:{time.Seconds:00}   ·   " +
            $"{state.Gold} gold   ·   potion {state.Potions}   ·   bunuh {state.Kills}";
        _zoneText.Text = $"Zona: {zoneName}   ·   checkpoint: {checkpointName}";
    }

    public override void _Process(double delta)
    {
        if (_toastTimer > 0)
        {
            _toastTimer -= delta;
            if (_toastTimer <= 0) _toast.Text = "";
        }
        AdvanceOverlay(delta, (float)Time.GetTicksMsec() / 1000f);
    }
}
