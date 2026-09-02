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

    public List<StaticCheckPathManifestEntry> StaticCheckPaths { get; set; } = [];

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

    public string? ScenePath { get; set; }

    public string SuccessMarker { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public List<string> CheckPaths { get; set; } = [];

    public List<ProductSmokeCheckpointManifestEntry> Checkpoints { get; set; } = [];

    public ProductSmokeStatePolicyManifestEntry? StatePolicy { get; set; }
}

internal sealed class ProductSmokeCheckpointManifestEntry
{
    public string Id { get; set; } = string.Empty;

    public string SuccessMarker { get; set; } = string.Empty;
}

internal sealed class ProductSmokeStatePolicyManifestEntry
{
    public bool Required { get; set; }

    public List<string> Compare { get; set; } = [];

    public List<string> MetricGauges { get; set; } = [];
}

internal sealed class VisualTargetManifestEntry
{
    public string Id { get; set; } = string.Empty;

    public string ScenePath { get; set; } = string.Empty;

    public string BaselinePath { get; set; } = string.Empty;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public string CaptureMode { get; set; } = "SemanticControl";

    public List<string> CheckPaths { get; set; } = [];

    public int ReadyFrames { get; set; } = 4;

    public float PixelTolerance { get; set; }

    public float MaxChangedPixelRatio { get; set; }

    public VisualPointerManifestEntry? Pointer { get; set; }
}

internal sealed class VisualPointerManifestEntry
{
    public float X { get; set; }

    public float Y { get; set; }
}

internal sealed class StaticCheckPathManifestEntry
{
    public string Pattern { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}
