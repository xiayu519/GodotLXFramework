using Godot;

namespace LX.Res;

public sealed class AssetGroup<T> where T : Resource
{
    private readonly IReadOnlyDictionary<string, AssetRef<T>> _assets;

    public AssetGroup(params (string Id, AssetRef<T> Asset)[] assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var indexed = new Dictionary<string, AssetRef<T>>(StringComparer.Ordinal);
        foreach (var (id, asset) in assets)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Grouped resource IDs must be non-empty.", nameof(assets));
            }
            if (!indexed.TryAdd(id, asset))
            {
                throw new ArgumentException($"Grouped resource ID '{id}' is duplicated.", nameof(assets));
            }
        }
        _assets = indexed;
    }

    public int Count => _assets.Count;

    public IEnumerable<string> Ids => _assets.Keys;

    public AssetRef<T> Get(string id) =>
        _assets.TryGetValue(id, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Resource group does not contain '{id}'.");

    public bool TryGet(string id, out AssetRef<T> asset) => _assets.TryGetValue(id, out asset);
}
