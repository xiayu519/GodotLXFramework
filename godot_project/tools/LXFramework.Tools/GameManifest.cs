namespace LXFramework.Tools;

internal sealed class GameManifest
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = "Game";

    public string SourceRoot { get; set; } = string.Empty;

    public string InitialWorldId { get; set; } = string.Empty;

    public List<WorldManifestEntry> Worlds { get; set; } = [];

    public List<ExportSmokeManifestEntry> ExportSmokes { get; set; } = [];
}

internal sealed class WorldManifestEntry
{
    public string Id { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string ScenePath { get; set; } = string.Empty;
}

internal sealed class ExportSmokeManifestEntry
{
    public string Id { get; set; } = string.Empty;

    public string Argument { get; set; } = string.Empty;

    public string SuccessMarker { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}
