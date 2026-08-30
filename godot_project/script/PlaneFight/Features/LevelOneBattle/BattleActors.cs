using Godot;
using GameData.plane_fight;

namespace PlaneFight.Features.LevelOneBattle;

internal enum BattleState
{
    Waiting,
    Waves,
    Clearing,
    BossWarning,
    Boss,
    Ending,
    Completed,
}

internal enum WeaponMode
{
    Standard,
    Twin,
    Laser,
    Spread,
}

internal sealed class EnemyActor
{
    public required BattleEnemyView Root { get; init; }
    public required Sprite2D Sprite { get; init; }
    public required string Id { get; init; }
    public required float MaxHp { get; init; }
    public required int Score { get; init; }
    public required float Speed { get; init; }
    public required float Radius { get; init; }
    public required float ContactDamage { get; init; }
    public required bool ChasesPlayer { get; init; }
    public required bool Fires { get; init; }
    public required float FireInterval { get; init; }
    public bool IsBoss { get; init; }
    public float Hp { get; set; }
    public float FireTimer { get; set; }
    public float SlowSeconds { get; set; }
    public float FrozenVisualAge { get; set; }
    public float HitFlashSeconds { get; set; }
}

internal sealed class ProjectileActor
{
    public required BattleProjectileView Root { get; init; }
    public required Vector2 Velocity { get; set; }
    public required float Damage { get; init; }
    public required float Radius { get; init; }
    public required bool FromPlayer { get; init; }
    public bool Homing { get; init; }
    public bool Freezes { get; init; }
    public bool Accelerates { get; init; }
    public float MaximumSpeed { get; init; }
    public float ElapsedSeconds { get; set; }
    public float LifeSeconds { get; set; }
    public BattleTrailView? Trail { get; init; }
    public int TrailPointLimit { get; init; }
}

internal sealed class PickupActor
{
    public required BattlePickupView Root { get; init; }
    public required Sprite2D Sprite { get; init; }
    public required PickupConfig Definition { get; init; }
    public Texture2D[]? Frames { get; init; }
    public float Age { get; set; }
}
