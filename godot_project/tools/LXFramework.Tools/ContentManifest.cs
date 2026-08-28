namespace LXFramework.Tools;

internal sealed class ContentManifest
{
    public int Version { get; set; } = 1;

    public List<ContentManifestEntry> Tables { get; set; } = [];
}

internal sealed class ContentManifestEntry
{
    public string Scope { get; set; } = ManifestScopes.Product;

    public string Id { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
