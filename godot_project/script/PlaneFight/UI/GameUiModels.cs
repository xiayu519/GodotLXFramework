namespace PlaneFight.UI;

/// <summary>开始弹窗返回给游戏流程的选择。</summary>
public enum StartChoice
{
    /// <summary>开始第一关。</summary>
    Start,
    /// <summary>退出当前程序。</summary>
    Exit,
}

/// <summary>结算弹窗返回给游戏流程的选择。</summary>
public enum ResultChoice
{
    /// <summary>从初始状态重新开始第一关。</summary>
    Restart,
    /// <summary>退出当前程序。</summary>
    Exit,
}

/// <summary>第一关最终结果。</summary>
public enum BattleOutcomeKind
{
    /// <summary>玩家生命耗尽。</summary>
    Defeat,
    /// <summary>第一关 Boss 被击破。</summary>
    Victory,
}

public sealed record BattleOutcome(
    BattleOutcomeKind Kind,
    int Score,
    int Gold,
    int Medals);

public sealed record ResultScreenPayload(BattleOutcome Outcome);

public sealed class BattleHudModel
{
    public float Hp { get; set; }
    public float MaxHp { get; set; }
    public int Score { get; set; }
    public int LevelScore { get; set; }
    public int PassScore { get; set; }
    public int Gold { get; set; }
    public int Medals { get; set; }
    public int MissileCount { get; set; }
    public int IceMissileCount { get; set; }
    public int NuclearBombCount { get; set; }
    public float NuclearBombCooldownSeconds { get; set; }
    public bool CanUseNuclearBomb { get; set; }
    public int ShieldCount { get; set; }
    public string WeaponName { get; set; } = "标准机炮";
    public float WeaponSeconds { get; set; }
    public bool BossVisible { get; set; }
    public float BossHp { get; set; }
    public float BossMaxHp { get; set; }
    public bool BossWarningVisible { get; set; }
    public float ShieldSeconds { get; set; }
    public float ShieldCooldownSeconds { get; set; }
    public Action? UseMissile { get; init; }
    public Action? UseIceMissile { get; init; }
    public Action? UseNuclearBomb { get; init; }
    public Action? UseShield { get; init; }
}
