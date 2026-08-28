using Godot;

namespace LX.Res;

public readonly record struct AssetRef<T> where T : Resource
{
    public AssetRef(string path, AssetCachePolicy cachePolicy = AssetCachePolicy.Transient)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Asset references must use a non-empty res:// path.", nameof(path));
        }
        if (!Enum.IsDefined(cachePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(cachePolicy));
        }

        Path = path;
        CachePolicy = cachePolicy;
    }

    public string Path { get; }

    public AssetCachePolicy CachePolicy { get; }
}
