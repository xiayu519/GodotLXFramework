namespace LXFramework.Tools;

internal sealed class AssetManifest
{
    public int Version { get; set; } = 1;

    public List<AssetManifestEntry> Assets { get; set; } = [];
}

internal sealed class AssetManifestEntry
{
    public string Scope { get; set; } = ManifestScopes.Product;

    public string Id { get; set; } = string.Empty;

    public string ResourceType { get; set; } = "Resource";

    public string Path { get; set; } = string.Empty;

    public string CachePolicy { get; set; } = "Transient";

    public string? Group { get; set; }

    public string? CatalogPartition { get; set; }
}
