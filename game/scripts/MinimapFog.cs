using Godot;

namespace RpgGame;

/// <summary>
/// Fog of war pada peta mini: chunk yang belum dijelajahi digelapkan.
/// Skala mengikuti ukuran TextureRect minimap dan ukuran peta dalam piksel.
/// </summary>
public sealed partial class MinimapFog : Control
{
    private static readonly Color Unexplored = new(0.04f, 0.04f, 0.06f, 0.82f);

    private ChunkSystem _chunks = null!;
    private Vector2 _worldSize;
    private int _revision = -1;

    public void Configure(ChunkSystem chunks, Vector2 worldSize)
    {
        _chunks = chunks;
        _worldSize = worldSize;
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_chunks is null || _chunks.Revision == _revision) return;
        _revision = _chunks.Revision;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_chunks is null || _worldSize.X <= 0 || _worldSize.Y <= 0) return;
        var scale = new Vector2(Size.X / _worldSize.X, Size.Y / _worldSize.Y);
        for (var index = 0; index < _chunks.TotalChunks; index++)
        {
            if (_chunks.IsExplored(index)) continue;
            var rect = _chunks.RectPx(index);
            DrawRect(new Rect2(rect.Position * scale, rect.Size * scale), Unexplored, filled: true);
        }
    }
}
