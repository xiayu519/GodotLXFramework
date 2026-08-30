namespace PlaneFight.Features.LevelOneBattle;

internal readonly record struct NuclearBombDetonated(
    float BaseDamage,
    bool ConsumedInventory,
    int TargetsHit);

internal readonly record struct BattleFinished(
    BattleState FinalState,
    int Score,
    int Gold,
    int Medals);
