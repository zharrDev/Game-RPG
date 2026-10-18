using System;
using System.Collections.Generic;

namespace RpgGame;

/// <summary>
/// Event bus bertipe + counter per jenis kejadian. Counter dipakai tes headless
/// untuk memastikan urutan kejadian deterministik tanpa harus mengintip node.
/// Pengiriman hanya dari sistem yang memiliki kejadian; efek visual (shake,
/// partikel, flash HUD) dipasang Game saat menyambung event.
/// </summary>
public sealed class EventBus
{
    public event Action<Mob>? MobKilled;
    public event Action<Mob, int, bool>? PlayerDamaged;
    public event Action<int>? LevelUp;
    public event Action<string>? QuestFlagSet;
    public event Action<string, string>? ZoneChanged;
    public event Action<string>? SaveWritten;

    private readonly Dictionary<string, int> _counts = new();

    /// <summary>Jumlah kejadian per jenis (untuk tes determinisme).</summary>
    public int Count(string kind) => _counts.TryGetValue(kind, out var count) ? count : 0;

    public int TotalCount { get; private set; }

    private void Bump(string kind)
    {
        _counts[kind] = Count(kind) + 1;
        TotalCount++;
    }

    public void RaiseMobKilled(Mob mob)
    {
        Bump("mob_killed");
        MobKilled?.Invoke(mob);
    }

    public void RaisePlayerDamaged(Mob mob, int damage, bool died)
    {
        Bump(died ? "player_died" : "player_damaged");
        PlayerDamaged?.Invoke(mob, damage, died);
    }

    public void RaiseLevelUp(int level)
    {
        Bump("level_up");
        LevelUp?.Invoke(level);
    }

    public void RaiseQuestFlagSet(string flag)
    {
        Bump("quest_flag");
        QuestFlagSet?.Invoke(flag);
    }

    public void RaiseZoneChanged(string id, string name)
    {
        Bump("zone_changed");
        ZoneChanged?.Invoke(id, name);
    }

    public void RaiseSaveWritten(string reason)
    {
        Bump("save_written");
        SaveWritten?.Invoke(reason);
    }

    /// <summary>Kosongkan counter (dipakai tes antar-seksi). Event tidak dicabut.</summary>
    public void ResetCounters()
    {
        _counts.Clear();
        TotalCount = 0;
    }
}
