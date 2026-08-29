using LX.Core.World;
using LX.Res;

namespace LX.World;

public sealed class WorldChunkManifest
{
    public int ChunkWidth { get; init; } = 1024;

    public int ChunkHeight { get; init; } = 1024;

    public List<WorldChunkEntry> Chunks { get; init; } = [];

    internal IReadOnlyDictionary<ChunkCoordinate, WorldChunkEntry> ValidateAndIndex()
    {
        if (ChunkWidth <= 0 || ChunkHeight <= 0)
        {
            throw new InvalidDataException("World chunk dimensions must be positive.");
        }

        var result = new Dictionary<ChunkCoordinate, WorldChunkEntry>();
        foreach (var entry in Chunks)
        {
            if (!GodotResourcePath.IsCanonical(entry.ScenePath, ".tscn"))
            {
                throw new InvalidDataException(
                    $"World chunk ({entry.X}, {entry.Y}) has an invalid scene path '{entry.ScenePath}'.");
            }

            var coordinate = new ChunkCoordinate(entry.X, entry.Y);
            if (!result.TryAdd(coordinate, entry))
            {
                throw new InvalidDataException($"World chunk coordinate ({entry.X}, {entry.Y}) is duplicated.");
            }
        }

        return result;
    }
}

public sealed record WorldChunkEntry(int X, int Y, string ScenePath);
