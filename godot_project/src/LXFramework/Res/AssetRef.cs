using Godot;

namespace LX.Res;

public readonly record struct AssetRef<T> where T : Resource
{
    public AssetRef(string path, AssetCachePolicy cachePolicy = AssetCachePolicy.Transient)
    {
        GodotResourcePath.Validate(path, nameof(path));
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
