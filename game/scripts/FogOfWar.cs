using Godot;

namespace RpgGame;

/// <summary>
/// Fog of war dunia: menutup chunk yang belum dijelajahi dengan warna gelap.
/// Chunk yang sudah dibuka disimpan ChunkSystem (ikut tersimpan di save).
/// Node ini tidak ikut y-sort dan digambar di atas dunia, di bawah HUD.
/// </summary>
public sealed partial class FogOfWar : Node2D
{
    private static readonly Color Unexplored = new(0.035f, 0.034f, 0.058f, 0.88f);

    private ChunkSystem _chunks = null!;
    private int _revision = -1;

    public void Configure(ChunkSystem chunks)
    {
        _chunks = chunks;
        ZAsRelative = false;
        ZIndex = 100;
        YSortEnabled = false;
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
        if (_chunks is null) return;
        for (var index = 0; index < _chunks.TotalChunks; index++)
        {
            if (_chunks.IsExplored(index)) continue;
            DrawRect(_chunks.RectPx(index), Unexplored, filled: true);
        }
    }
}
