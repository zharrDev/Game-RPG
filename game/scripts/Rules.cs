using System;
using System.Collections.Generic;

namespace RpgGame;

/// <summary>
/// Aturan main (satu tempat, dipakai HUD, aktor, dan tes headless).
/// </summary>
public static class Rules
{
    public const double WalkSpeed = 56;
    public const double RunSpeed = 96;
    public const double AttackRange = 24;
    public const double AttackArcDot = 0.35;     // cos sudut pandang untuk kena serangan
    public const double AttackCooldown = 0.45;
    public const double AttackHitDelay = 0.10;   // damage terjadi setelah animasi mengayun
    public const double JumpTime = 0.36;         // lompat (pose AI jump_1..3)
    public const double SkillRange = 48;
    public const double SkillCost = 30;
    public const double SkillCooldown = 1.1;
    public const double EnergyRegen = 14;
    public const double MaxEnergy = 100;
    public const int SkillUnlockLevel = 3;
    public const double InvulnerableTime = 0.7;
    public const int PotionHeal = 40;        // HP yang dipulihkan satu potion
    public const int PotionPrice = 25;       // harga di pasar
    public const int MaxPotions = 9;
    public const double DeathDuration = 1.6;
    public const double InteractRange = 26;
    public const double AutosaveInterval = 30;
    public const int SlotCount = 3;

    public static int MaxHp(int level) => 100 + (level - 1) * 15;
    public static int XpToNext(int level) => 40 + (level - 1) * 45;
    public static int AttackDamage(int level) => 10 + level * 2;
    public static int SkillDamage(int level) => 26 + level * 3;
    public static int SkillLevelFor(int level) => Math.Clamp(level - 2, 0, 3);

    public sealed record EnemyType(string Id, string Display, int Hp, int Damage, int Xp,
        double Speed, double Detect, bool Boss);

    public static readonly Dictionary<string, EnemyType> Enemies = new()
    {
        ["slime"] = new EnemyType("slime", "Slime", 24, 6, 12, 26, 78, false),
        ["bat"] = new EnemyType("bat", "Kelelawar", 18, 5, 14, 44, 104, false),
        ["warden"] = new EnemyType("warden", "Ruin Warden", 120, 14, 90, 34, 150, true),
    };

    public static EnemyType Enemy(string id) =>
        Enemies.TryGetValue(id, out var type) ? type : Enemies["slime"];

    /// <summary>Nama animasi hero untuk arah + aksi.</summary>
    public static string Animation(string action, string facing)
    {
        var dir = facing is "down" or "up" ? facing : "side";
        return action switch
        {
            "walk" => $"walk_{dir}",
            "run" => $"run_{dir}",
            "idle" => $"idle_{dir}",
            _ => action,
        };
    }

    public static bool FacingIsMirrored(string facing) => facing == "left";
}
