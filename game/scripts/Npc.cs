using Godot;

namespace RpgGame;

/// <summary>
/// Warga desa: sprite idle 2 frame + penanda "!" kalau ada quest yang belum
/// selesai. NPC tidak ikut bertarung dan tidak memblokir jalan.
/// </summary>
public sealed partial class Npc : Node2D
{
    public readonly NpcInfo Info;
    public bool QuestPending { get; private set; }

    private readonly AnimatedSprite2D _sprite;
    private readonly Label _marker;

    public Npc(World world, NpcInfo info)
    {
        Info = info;
        Name = $"Npc_{info.Id}";
        Position = new Vector2((info.Col + 0.5f) * World.TileSize, (info.Row + 1f) * World.TileSize);

        AddChild(new Sprite2D
        {
            Name = "Shadow",
            Texture = world.ShadowTexture,
            Centered = true,
            Position = new Vector2(0, 1),
            ZIndex = -1,
            Modulate = new Color(1, 1, 1, 0.45f),
        });

        var animation = info.Facing == "side" ? "idle_side" : "idle_down";
        var frames = world.NpcFrames.TryGetValue(info.Id, out var found) ? found : null;
        if (frames is not null && !frames.HasAnimation(animation)) animation = "idle_down";
        _sprite = new AnimatedSprite2D
        {
            Name = "Sprite",
            SpriteFrames = frames,
            Centered = false,
            Offset = new Vector2(-world.Npcs.Foot.X, -world.Npcs.Foot.Y),
            FlipH = info.Facing == "side",
        };
        if (frames is not null && frames.HasAnimation(animation)) _sprite.Animation = animation;
        AddChild(_sprite);
        _sprite.Play();

        _marker = new Label
        {
            Name = "QuestMarker",
            Text = "!",
            Position = new Vector2(-2, -36),
            Visible = false,
        };
        if (Hud.PixelFont is not null)
        {
            _marker.AddThemeFontOverride("font", Hud.PixelFont);
            _marker.AddThemeFontSizeOverride("font_size", 8);
        }
        else
        {
            _marker.AddThemeFontSizeOverride("font_size", 10);
        }
        _marker.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.35f));
        _marker.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.1f, 0.08f));
        _marker.AddThemeConstantOverride("outline_size", 2);
        AddChild(_marker);
    }

    public void SetQuestPending(bool pending)
    {
        QuestPending = pending;
        _marker.Visible = pending;
    }

    /// <summary>Menghadap pemain saat diajak bicara (sprite side di-flip).</summary>
    public void Face(Vector2 target)
    {
        if (!_sprite.SpriteFrames!.HasAnimation("idle_side")) return;
        _sprite.FlipH = target.X < Position.X;
        if (_sprite.Animation != "idle_side")
        {
            _sprite.Animation = "idle_side";
            _sprite.Play();
        }
    }
}
