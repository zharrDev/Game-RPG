using System.Collections.Generic;
using Godot;

namespace RpgGame;

/// <summary>
/// Nyala api untuk api gameplay (M4): kolam AnimatedSprite2D yang mengikuti
/// sel api dari FireSystem. Bara memakai kolam FireAmbience yang sudah ada.
/// </summary>
public sealed partial class FireVisuals : Node2D
{
    public const int PoolSize = 64;

    private readonly List<AnimatedSprite2D> _flames = new();
    private FireSystem _fire = null!;
    private Game _game = null!;
    private double _emberTimer;
    private uint _seed = 0x2468ACE1u;

    public int VisibleFlames { get; private set; }

    public void Configure(Game game)
    {
        _game = game;
        _fire = game.Fire!;
        var texture = Assets.LoadTexture(Assets.FxFlamePng);
        var frames = new SpriteFrames();
        frames.AddAnimation("burn");
        frames.SetAnimationSpeed("burn", 11);
        frames.SetAnimationLoop("burn", true);
        for (var i = 0; i < 4; i++)
        {
            frames.AddFrame("burn", new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(i * 16, 0, 16, 16),
            });
        }
        for (var i = 0; i < PoolSize; i++)
        {
            var flame = new AnimatedSprite2D
            {
                Name = $"Fire_{i}",
                SpriteFrames = frames,
                Animation = "burn",
                Autoplay = "burn",
                Offset = new Vector2(0, 8),
                Visible = false,
                ZIndex = 30,
            };
            flame.Play("burn");
            AddChild(flame);
            _flames.Add(flame);
        }
    }

    public void Sync()
    {
        if (_fire is null) return;
        var index = 0;
        var embers = 0;
        foreach (var cell in _fire.Cells())
        {
            if (index >= _flames.Count) break;
            var flame = _flames[index];
            flame.Visible = true;
            flame.Position = cell.Position;
            flame.Scale = Vector2.One * (0.5f + cell.Intensity * 0.65f);
            flame.Modulate = new Color(1f, 0.92f - cell.Intensity * 0.18f, 0.8f, 0.9f + cell.Intensity * 0.1f);
            index++;
            embers++;
        }
        for (var i = index; i < _flames.Count; i++) _flames[i].Visible = false;
        VisibleFlames = index;

        if (embers == 0 || _game.Ambience is null) return;
        _emberTimer += GetProcessDeltaTime();
        if (_emberTimer < 0.22) return;
        _emberTimer = 0;
        foreach (var cell in _fire.Cells())
        {
            _seed = _seed * 1664525u + 1013904223u;
            if ((_seed >> 8) % 100 > 22) continue;
            _game.Ambience.ForceEmber(cell.Position + new Vector2(((int)(_seed >> 16) % 13) - 6, 0));
        }
    }

    public override void _Process(double delta) => Sync();
}
