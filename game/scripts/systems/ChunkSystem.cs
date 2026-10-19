using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RpgGame;

/// <summary>
/// Chunk dunia: membagi peta menjadi kotak 8x8 tile untuk dua hal —
/// (1) aktivasi aktor (musuh/NPC jauh tidak di-tick) dan (2) fog of war
/// (chunk yang sudah dijelajahi disimpan di save).
/// </summary>
public sealed class ChunkSystem
{
    public const int ChunkSize = 8;
    public const int RevealRadius = 1;
    public const int ActiveRadius = 3;

    private readonly HashSet<int> _explored = new();

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int TotalChunks => Cols * Rows;
    public int Current { get; private set; } = -1;
    public int Revision { get; private set; }
    public int ExploredCount => _explored.Count;
    public float ExploredPercent => TotalChunks == 0 ? 0f : ExploredCount * 100f / TotalChunks;

    /// <summary>Dipanggil sekali setelah World dibangun (ukuran peta diketahui).</summary>
    public void Configure(int widthTiles, int heightTiles)
    {
        Cols = Math.Max(1, (widthTiles + ChunkSize - 1) / ChunkSize);
        Rows = Math.Max(1, (heightTiles + ChunkSize - 1) / ChunkSize);
        Current = -1;
        Revision++;
    }

    public int ColOf(int index) => Cols == 0 ? 0 : index % Cols;
    public int RowOf(int index) => Cols == 0 ? 0 : index / Cols;
    public bool InBounds(int index) => index >= 0 && index < TotalChunks;

    /// <summary>Indeks chunk dari posisi piksel dunia.</summary>
    public int IndexAt(Vector2 position)
    {
        if (Cols == 0 || Rows == 0) return -1;
        var col = Math.Clamp((int)(position.X / (ChunkSize * World.TileSize)), 0, Cols - 1);
        var row = Math.Clamp((int)(position.Y / (ChunkSize * World.TileSize)), 0, Rows - 1);
        return row * Cols + col;
    }

    /// <summary>Batas chunk dalam piksel (untuk fog overlay & peta).</summary>
    public Rect2 RectPx(int index)
    {
        var size = ChunkSize * World.TileSize;
        return new Rect2(ColOf(index) * size, RowOf(index) * size, size, size);
    }

    public bool IsExplored(int index) => _explored.Contains(index);

    /// <summary>
    /// Perbarui chunk aktif & buka chunk di sekitar pemain. Mengembalikan true
    /// kalau ada chunk baru (memicu redraw fog / minimap).
    /// </summary>
    public bool Update(Vector2 playerPosition)
    {
        var index = IndexAt(playerPosition);
        if (index < 0) return false;
        var changed = false;
        if (index != Current)
        {
            Current = index;
            Revision++;
        }
        var centerCol = ColOf(index);
        var centerRow = RowOf(index);
        for (var dy = -RevealRadius; dy <= RevealRadius; dy++)
        {
            for (var dx = -RevealRadius; dx <= RevealRadius; dx++)
            {
                var col = centerCol + dx;
                var row = centerRow + dy;
                if (col < 0 || row < 0 || col >= Cols || row >= Rows) continue;
                if (!_explored.Add(row * Cols + col)) continue;
                changed = true;
                Revision++;
                ChunkExplored?.Invoke(row * Cols + col);
            }
        }
        return changed;
    }

    /// <summary>True kalau posisi ini berada di chunk dekat pemain (aktor boleh tick).</summary>
    public bool IsActive(Vector2 position)
    {
        var index = IndexAt(position);
        if (index < 0 || Current < 0) return true;
        var dx = Math.Abs(ColOf(index) - ColOf(Current));
        var dy = Math.Abs(RowOf(index) - RowOf(Current));
        return dx <= ActiveRadius && dy <= ActiveRadius;
    }

    public event Action<int>? ChunkExplored;

    /// <summary>Daftar chunk yang sudah dijelajahi (urut naik, untuk save deterministik).</summary>
    public List<int> Snapshot() => _explored.OrderBy(i => i).ToList();

    /// <summary>Pulihkan dari save. Indeks di luar peta diabaikan.</summary>
    public void Restore(IEnumerable<int> indices)
    {
        _explored.Clear();
        foreach (var index in indices)
            if (InBounds(index)) _explored.Add(index);
        Revision++;
    }

    public void RevealAll()
    {
        for (var i = 0; i < TotalChunks; i++) _explored.Add(i);
        Revision++;
    }
}
