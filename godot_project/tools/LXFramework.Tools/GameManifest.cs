using System.Text.Json.Serialization;

namespace LXFramework.Tools;

internal sealed class GameManifest
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = "Game";

    public string SourceRoot { get; set; } = string.Empty;

    public string InitialWorldId { get; set; } = string.Empty;

    public List<WorldManifestEntry> Worlds { get; set; } = [];

    public List<ProductSmokeManifestEntry> ProductSmokes { get; set; } = [];

    // Compatibility for manifests created before product smokes also ran in Debug validation.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ProductSmokeManifestEntry>? ExportSmokes { get; set; }

    public List<VisualTargetManifestEntry> VisualTargets { get; set; } = [];

    public IReadOnlyList<ProductSmokeManifestEntry> GetProductSmokes()
    {
        if (ProductSmokes.Count != 0 && ExportSmokes is { Count: > 0 })
        {
            throw new InvalidDataException(
                "Game manifest cannot declare both 'productSmokes' and legacy 'exportSmokes'.");
        }

        return ProductSmokes.Count != 0 ? ProductSmokes : ExportSmokes ?? [];
    }
}

internal sealed class WorldManifestEntry
{
    public string Id { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string ScenePath { get; set; } = string.Empty;
}

internal sealed class ProductSmokeManifestEntry
{
    public string Id { get; set; } = string.Empty;

    public string Argument { get; set; } = string.Empty;

    public string SuccessMarker { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}

internal sealed class VisualTargetManifestEntry
{
    public string Id { get; set; } = string.Empty;

    public string ScenePath { get; set; } = string.Empty;

    public string BaselinePath { get; set; } = string.Empty;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;
}
