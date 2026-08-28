namespace LXFramework.Tools;

internal sealed class UIManifest
{
    public int Version { get; set; } = 1;

    public List<UIManifestEntry> Screens { get; set; } = [];
}

internal sealed class UIManifestEntry
{
    public string Scope { get; set; } = ManifestScopes.Product;

    public string Id { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string ScenePath { get; set; } = string.Empty;

    public string Layer { get; set; } = "Screen";

    public string CachePolicy { get; set; } = "Transient";

    public string CoverPolicy { get; set; } = "KeepVisible";

    public string InputPolicy { get; set; } = "Normal";

    public string FocusPolicy { get; set; } = "Preserve";
}
