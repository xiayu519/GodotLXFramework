namespace LXFramework.Tools;

internal sealed class InputManifest
{
    public int Version { get; set; } = 1;

    public List<InputManifestAction> Actions { get; set; } = [];
}

internal sealed class InputManifestAction
{
    public string Scope { get; set; } = ManifestScopes.Product;

    public string Id { get; set; } = string.Empty;

    public List<InputManifestRoute> Routes { get; set; } = [];
}

internal sealed class InputManifestRoute
{
    public string GodotAction { get; set; } = string.Empty;

    public string? DefaultPhysicalKey { get; set; }
}
