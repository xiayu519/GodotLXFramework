using LX.Res;
using LX.Core.Lifetime;
using LX.Core.World;
using Godot;

namespace LX.World;

internal sealed class PackedSceneWorldChunkSource : IWorldChunkSource
{
    private readonly AssetRegistry _assets;
    private readonly IReadOnlyDictionary<ChunkCoordinate, WorldChunkEntry> _definitions;

    public PackedSceneWorldChunkSource(AssetRegistry assets, WorldChunkManifest manifest)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        ArgumentNullException.ThrowIfNull(manifest);
        _definitions = manifest.ValidateAndIndex();
        ChunkWidth = manifest.ChunkWidth;
        ChunkHeight = manifest.ChunkHeight;
        Coordinates = _definitions.Keys.ToArray();
    }

    public int ChunkWidth { get; }

    public int ChunkHeight { get; }

    public IReadOnlyCollection<ChunkCoordinate> Coordinates { get; }

    public async ValueTask<Node> InstantiateAsync(
        ChunkCoordinate coordinate,
        LifetimeScope lifetime,
        CancellationToken cancellationToken)
    {
        if (!_definitions.TryGetValue(coordinate, out var definition))
        {
            throw new KeyNotFoundException($"World chunk ({coordinate.X},{coordinate.Y}) is unavailable.");
        }
        var lease = await _assets.AcquireAsync<PackedScene>(
            definition.ScenePath,
            AssetCachePolicy.Transient,
            cancellationToken);
        lifetime.Own(lease);
        return lease.Resource.Instantiate();
    }

    public void PurgeIdleCache() => _assets.PurgeIdleCache();
}
