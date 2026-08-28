namespace LXFramework.Tools;

internal sealed class FeatureManifest
{
    public int Version { get; set; } = 1;

    public List<FeatureManifestEntry> Features { get; set; } = [];
}

internal sealed class FeatureManifestEntry
{
    public string Scope { get; set; } = ManifestScopes.Product;

    public string Id { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string ScenePath { get; set; } = string.Empty;
}
