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
