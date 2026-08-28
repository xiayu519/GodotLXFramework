using LX.Core.Lifetime;
using LX.Core.World;
using Godot;

namespace LX.World;

/// <summary>
/// Supplies chunk nodes while WorldChunkStreamer remains the single owner of
/// visibility, lifecycle, metrics and main-thread ordering.
/// </summary>
public interface IWorldChunkSource : IAsyncDisposable
{
    int ChunkWidth { get; }

    int ChunkHeight { get; }

    IReadOnlyCollection<ChunkCoordinate> Coordinates { get; }

    bool IsAvailable(ChunkCoordinate coordinate) => Coordinates.Contains(coordinate);

    ChunkCoordinate Canonicalize(ChunkCoordinate coordinate) => coordinate;

    ValueTask<Node> InstantiateAsync(
        ChunkCoordinate coordinate,
        LifetimeScope lifetime,
        CancellationToken cancellationToken);

    void PurgeIdleCache()
    {
    }

    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
}
