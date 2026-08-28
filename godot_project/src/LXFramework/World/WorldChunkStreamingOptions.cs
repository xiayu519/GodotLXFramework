namespace LX.World;

/// <summary>
/// Frame budgets for a focus change. Small defaults favor stable frame time;
/// callers may raise them during loading screens or teleport transitions.
/// </summary>
public sealed record WorldChunkStreamingOptions
{
    public int Radius { get; init; } = 1;

    public int MaxLoadsPerFrame { get; init; } = 1;

    public int MaxUnloadsPerFrame { get; init; } = 4;

    /// <summary>
    /// 在 Godot 主线程报告本次焦点变化已完成和总计的真实区块加载/卸载操作数。
    /// 第一次回调始终报告 0；没有操作或焦点未变化时不会回调。
    /// </summary>
    public Action<int, int>? Progress { get; init; }

    internal void Validate()
    {
        if (Radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Radius));
        }
        if (MaxLoadsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLoadsPerFrame));
        }
        if (MaxUnloadsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxUnloadsPerFrame));
        }
    }
}
