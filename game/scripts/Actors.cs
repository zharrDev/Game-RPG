using System;
using Godot;

namespace RpgGame;

/// <summary>Input satu frame; bisa diisi dari keyboard ataupun dari tes.</summary>
public struct InputState
{
    public float X, Y;
    public bool Run;
    public bool AttackPressed;
    public bool SkillPressed;
    public bool JumpPressed;
    public bool AnyMovement => Math.Abs(X) > 0.01f || Math.Abs(Y) > 0.01f;
}

public sealed partial class Player : Node2D
{
    public readonly GameState State = new();
    public string Facing = "down";
    public string Action = "idle";
    public double Energy = Rules.MaxEnergy;
    public double Playtime;
    /// <summary>Paparan asap 0..1 dari FireSystem; menguras napas dan menahan regen.</summary>
    public float SmokeExposure;
    /// <summary>F1: pengali kuras asap (&lt;1 saat memakai masker kain).</summary>
    public float SmokeDrainScale = 1f;
    /// <summary>F1: pengali regen energi (&gt;1 saat hujan menyegarkan).</summary>
    public float EnergyRegenScale = 1f;
    private bool _exhausted;

    public double AttackTimer, SkillTimer, HurtTimer, InvulnerableTimer, DeathTimer, JumpTimer;
    public int LevelFlash;

    private AnimatedSprite2D _sprite = null!;
    private Sprite2D _shadow = null!;
    private bool _strikeDone;
    private readonly Vector2 _half = new(5, 5);   // tapak kaki: 11x6 px, titik kaki di baris terbawah

    /// <summary>(posisi, arah hadap, damage, jenis) — dipakai Game untuk menghitung musuh yang kena.</summary>
    public event Action<Vector2, string, int, string>? Strike;
    public event Action<int>? LeveledUp;

    public bool Alive => Action != "death";
    public bool SkillUnlocked => State.SkillLevel >= 1;
    public bool Busy => Action is "attack" or "skill" or "hurt" or "jump";
    public Vector2 Feet => Position;
    public Vector2 Center => Position - new Vector2(0, 11);

    public Player(World world)
    {
        Name = "Player";
        _shadow = new Sprite2D
        {
            Name = "Shadow",
            Texture = world.ShadowTexture,
            Centered = true,
            Position = new Vector2(0, 1),
            ZIndex = -1,
            Modulate = new Color(1, 1, 1, 0.55f),
        };
        AddChild(_shadow);

        // Satu sumber frame: world.HeroFrames (atlas hero_atlas.png). Jangan bikin
        // salinan sendiri — dulu di sini dipakai TilesetTexture sehingga sprite
        // hero mengambil region dari atlas yang salah.
        _sprite = new AnimatedSprite2D
        {
            Name = "Sprite",
            SpriteFrames = world.HeroFrames,
            Centered = false,
            Offset = new Vector2(-world.Hero.Foot.X, -world.Hero.Foot.Y),
        };
        AddChild(_sprite);
        _sprite.Animation = "idle_down";
        _sprite.Play();
    }

    public void SyncFromState()
    {
        Facing = FacingFromInt(State.Facing);
        _sprite.FlipH = Rules.FacingIsMirrored(Facing);
        _sprite.Animation = Rules.Animation(Action, Facing);
        _sprite.Play();
    }

    private static string FacingFromInt(int facing) => facing < 0 ? "left" : "right";

    private static string FacingFromVector(Vector2 dir)
    {
        if (Math.Abs(dir.X) > Math.Abs(dir.Y))
            return dir.X < 0 ? "left" : "right";
        return dir.Y < 0 ? "up" : "down";
    }

    private void SetAction(string action)
    {
        Action = action;
        var animation = Rules.Animation(action, Facing);
        if (_sprite.SpriteFrames!.HasAnimation(animation) && _sprite.Animation != animation)
        {
            _sprite.Animation = animation;
            _sprite.Play();
        }
    }

    public void Tick(double delta, InputState input, CollisionGrid grid)
    {
        Playtime += delta;
        var runningNow = input.Run && input.AnyMovement && !_exhausted && Energy > 0;
        if (!runningNow && SmokeExposure <= 0.05f)
            Energy = Math.Min(Rules.MaxEnergy, Energy + Rules.EnergyRegen * EnergyRegenScale * delta);
        if (SmokeExposure > 0.05f)
            Energy = Math.Max(0, Energy - Rules.SmokeEnergyDrain * SmokeExposure * SmokeDrainScale * delta);
        AttackTimer = Math.Max(0, AttackTimer - delta);
        SkillTimer = Math.Max(0, SkillTimer - delta);
        HurtTimer = Math.Max(0, HurtTimer - delta);
        InvulnerableTimer = Math.Max(0, InvulnerableTimer - delta);
        JumpTimer = Math.Max(0, JumpTimer - delta);
        if (LevelFlash > 0) LevelFlash = Math.Max(0, LevelFlash - (int)(delta * 10) - 1);

        if (Action == "death")
        {
            DeathTimer += delta;
            _shadow.Modulate = new Color(1, 1, 1, (float)Math.Max(0, 0.55 - DeathTimer));
            return;
        }

        if (Action == "hurt")
        {
            if (HurtTimer <= 0) SetAction("idle");
            return;
        }

        if (Action == "jump")
        {
            if (JumpTimer <= 0) SetAction("idle");
            return;
        }

        if (Action == "attack")
        {
            if (!_strikeDone && AttackTimer <= Rules.AttackCooldown - Rules.AttackHitDelay)
            {
                _strikeDone = true;
                Strike?.Invoke(Center, Facing, Rules.AttackDamage(State.Level), "attack");
            }
            if (AttackTimer <= 0) SetAction("idle");
            return;
        }

        if (Action == "skill")
        {
            if (!_strikeDone && SkillTimer <= Rules.SkillCooldown - 0.18)
            {
                _strikeDone = true;
                Strike?.Invoke(Center, Facing, Rules.SkillDamage(State.Level), "skill");
            }
            if (SkillTimer <= 0) SetAction("idle");
            return;
        }

        // aksi baru
        if (input.AttackPressed && AttackTimer <= 0)
        {
            AttackTimer = Rules.AttackCooldown;
            _strikeDone = false;
            SetAction("attack");
            return;
        }
        if (input.SkillPressed && SkillUnlocked && SkillTimer <= 0 && Energy >= Rules.SkillCost)
        {
            Energy -= Rules.SkillCost;
            SkillTimer = Rules.SkillCooldown;
            _strikeDone = false;
            SetAction("skill");
            return;
        }
        if (input.JumpPressed && JumpTimer <= 0)
        {
            JumpTimer = Rules.JumpTime;
            SetAction("jump");
            return;
        }

        var dir = new Vector2(input.X, input.Y);
        if (dir.LengthSquared() > 0.0001f)
        {
            dir = dir.Normalized();
            Facing = FacingFromVector(dir);
            State.Facing = Facing == "left" ? -1 : 1;
            var running = input.Run && !_exhausted && Energy > 0;
            if (running) Energy = Math.Max(0, Energy - Rules.RunEnergyDrain * delta);
            if (Energy <= 0.01)
            {
                _exhausted = true;
                running = false;
            }
            else if (_exhausted && Energy >= 25)
            {
                _exhausted = false;
            }
            var speed = running ? Rules.RunSpeed : Rules.WalkSpeed;
            var next = grid.Move(Position, dir * (float)(speed * delta), _half);
            Position = grid.Clamp(next, _half);
            SetAction(running ? "run" : "walk");
        }
        else
        {
            SetAction("idle");
        }
        _sprite.FlipH = Rules.FacingIsMirrored(Facing);
        _shadow.Scale = new Vector2(1f, Action is "walk" or "run" ? 0.85f : 1f);
    }

    public bool TakeDamage(int amount)
    {
        if (Action == "death" || InvulnerableTimer > 0) return false;
        State.Hp = Math.Max(0, State.Hp - amount);
        InvulnerableTimer = Rules.InvulnerableTime;
        HurtTimer = 0.3;
        if (State.Hp <= 0)
        {
            Action = "death";
            DeathTimer = 0;
            SetAction("death");
            return true;
        }
        SetAction("hurt");
        return false;
    }

    public bool AddXp(int amount)
    {
        State.Xp += amount;
        var leveled = false;
        while (State.Xp >= Rules.XpToNext(State.Level))
        {
            State.Xp -= Rules.XpToNext(State.Level);
            State.Level++;
            State.MaxHp = Rules.MaxHp(State.Level);
            State.Hp = State.MaxHp;
            State.SkillLevel = Rules.SkillLevelFor(State.Level);
            if (State.SkillLevel >= 1 && !State.Skills.Contains("aura_slash"))
                State.Skills.Add("aura_slash");
            LevelFlash = 12;
            leveled = true;
            LeveledUp?.Invoke(State.Level);
        }
        return leveled;
    }

    /// <summary>Pulihkan dari state tersimpan (posisi, hp, level, skill).</summary>
    public void ApplyState(GameState state)
    {
        Position = new Vector2((float)state.X, (float)state.Y);
        Energy = Rules.MaxEnergy;
        Action = "idle";
        AttackTimer = SkillTimer = HurtTimer = InvulnerableTimer = DeathTimer = JumpTimer = 0;
        Facing = FacingFromInt(state.Facing);
        SyncFromState();
    }

    public void UpdateFacingVisual()
    {
        _sprite.FlipH = Rules.FacingIsMirrored(Facing);
    }
}

public sealed partial class Mob : Node2D
{
    public Rules.EnemyType Stats = Rules.Enemy("slime");
    public string Zone = "";
    public int Hp;
    public bool Dead;
    public string Action = "idle";
    public double AttackTimer, HurtTimer, DeathTimer, WanderTimer;
    public Vector2 Home;
    public int Id;

    private AnimatedSprite2D _sprite = null!;
    private Sprite2D _shadow = null!;
    private readonly Vector2 _half = new(5, 5);
    private Vector2 _wanderDir = Vector2.Zero;

    public event Action<Mob>? Removed;

    public Mob(World world, EnemySpawnInfo spawn, int id)
    {
        Stats = Rules.Enemy(spawn.Type);
        Zone = spawn.Zone;
        Id = id;
        Name = $"{Stats.Id}_{id}";
        Home = new Vector2((spawn.Col + 0.5f) * World.TileSize, (spawn.Row + 0.5f) * World.TileSize);
        Position = Home;
        Hp = Stats.Hp;
        AddToGroup("mob");

        _shadow = new Sprite2D
        {
            Name = "Shadow",
            Texture = world.ShadowTexture,
            Centered = true,
            Position = new Vector2(0, 1),
            ZIndex = -1,
            Modulate = new Color(1, 1, 1, 0.45f),
        };
        AddChild(_shadow);

        var frames = world.EnemyFrames.TryGetValue(Stats.Id, out var f) ? f : null;
        _sprite = new AnimatedSprite2D
        {
            Name = "Sprite",
            SpriteFrames = frames,
            Centered = false,
            Offset = new Vector2(-world.Enemies.Foot.X, -world.Enemies.Foot.Y),
        };
        AddChild(_sprite);
        if (frames is not null && frames.HasAnimation("idle"))
        {
            _sprite.Animation = "idle";
            _sprite.Play();
        }
    }

    private void SetAction(string action)
    {
        Action = action;
        var frames = _sprite.SpriteFrames;
        if (frames is null || !frames.HasAnimation(action)) return;
        if (_sprite.Animation != action)
        {
            _sprite.Animation = action;
            _sprite.Play();
        }
    }

    public void Tick(double delta, Vector2 playerPos, bool playerAlive, CollisionGrid grid,
        Action<Mob, int> damagePlayer)
    {
        AttackTimer = Math.Max(0, AttackTimer - delta);
        if (HurtTimer > 0) HurtTimer = Math.Max(0, HurtTimer - delta);
        if (Dead)
        {
            DeathTimer += delta;
            _shadow.Modulate = new Color(1, 1, 1, (float)Math.Max(0, 0.45 - DeathTimer * 0.5));
            if (DeathTimer > 1.1)
            {
                Removed?.Invoke(this);
                QueueFree();
            }
            return;
        }
        if (HurtTimer > 0)
        {
            SetAction("hurt");
            return;
        }

        var toPlayer = playerPos - Position;
        var distance = toPlayer.Length();
        var chase = playerAlive && distance < Stats.Detect && Position.DistanceTo(Home) < Stats.Detect * 2.2f;
        var maxLeash = Stats.Detect * 2.2f;

        if (chase && distance < 17)
        {
            SetAction("attack");
            _sprite.FlipH = toPlayer.X < 0;
            if (AttackTimer <= 0)
            {
                AttackTimer = Stats.Boss ? 0.9 : 1.2;
                damagePlayer(this, Stats.Damage);
            }
            return;
        }

        Vector2 dir;
        if (chase)
        {
            dir = toPlayer.Normalized();
            SetAction("move");
        }
        else
        {
            WanderTimer -= delta;
            if (WanderTimer <= 0)
            {
                WanderTimer = 1.2 + (Id % 5) * 0.35;
                var angle = (Id * 1.7 + Position.X * 0.01 + Position.Y * 0.013) % (Math.PI * 2);
                _wanderDir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            }
            dir = _wanderDir;
            if (Position.DistanceTo(Home) > 40)
                dir = (Home - Position).Normalized();
            SetAction(dir.LengthSquared() > 0.01f ? "move" : "idle");
        }

        if (dir.LengthSquared() > 0.0001f)
        {
            var speed = Stats.Speed * (Stats.Boss ? 1.0 : 1.0);
            var next = grid.Move(Position, dir.Normalized() * (float)(speed * delta), _half);
            if (next.DistanceTo(Position) < 0.01f && chase)
            {
                // mentok: coba geser tegak lurus biar tidak nyangkut di dinding
                var slide = new Vector2(-dir.Y, dir.X) * (float)(speed * delta);
                next = grid.Move(Position, slide, _half);
            }
            Position = grid.Clamp(next, _half);
            _sprite.FlipH = dir.X < 0;
        }
    }

    /// <summary>True kalau mob mati karena serangan ini.</summary>
    public bool TakeDamage(int amount)
    {
        if (Dead) return false;
        Hp -= amount;
        if (Hp <= 0)
        {
            Dead = true;
            DeathTimer = 0;
            SetAction("death");
            return true;
        }
        HurtTimer = 0.18;
        SetAction("hurt");
        return false;
    }
}
