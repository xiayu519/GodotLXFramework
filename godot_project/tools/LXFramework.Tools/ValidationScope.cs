namespace LXFramework.Tools;

// Incremental checks never stand in for the explicit full validation gate.
// Unknown paths and shared tooling deliberately retain all static checks.
internal sealed class ValidationScope(IReadOnlySet<string>? paths)
{
    private static readonly string[] IncrementalRoots =
    [
        "src/LXFramework.Core", "src/LXFramework", "script", "scene", "content",
        "tests/LXFramework.Core.Tests", "game_design/schema", "game_design/data",
    ];

    public bool Full { get; } = paths is null || paths.Any(path =>
        !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
        !IncrementalRoots.Any(root => Related(path, root)));

    public bool Touches(params string[] roots) => Full ||
        paths!.Any(path => roots.Any(root => Related(path, root)));

    public bool Source => Full || paths!.Any(path =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".cs.uid", StringComparison.OrdinalIgnoreCase) ||
        !Path.HasExtension(path));

    public bool Architecture => Source || Touches("content/game", "content/features", "scene/world", "scene/features");
    public bool PublicApi => Touches("src/LXFramework.Core", "src/LXFramework");
    public bool Luban => Touches("game_design", "content/data/luban", "src/LXFramework.Core/Data",
        "src/LXFramework/Content/ContentService.cs") ||
        paths!.Any(path => path.Contains("/Generated/Luban", StringComparison.OrdinalIgnoreCase));
    public bool Ui => Touches("content/ui", "scene/ui") || (Source && Touches("script"));
    public bool Registrations => Touches("content", "scene");
    public bool Generated => Full || paths!.Any(path =>
        path.EndsWith("-manifest.json", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/Generated", StringComparison.OrdinalIgnoreCase) ||
        Related(path, "scene/ui") || !Path.HasExtension(path));

    public bool Resources(string root)
    {
        if (Touches("content/res")) { return true; }
        var manifest = ToolFiles.ReadJson<AssetManifest>(Path.Combine(root, "content", "res", "res-manifest.json"));
        return manifest.Assets.Any(asset => paths!.Any(path =>
            Related(path, asset.Path.Replace("res://", "", StringComparison.Ordinal))));
    }

    internal static bool Related(string path, string root) =>
        string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) ||
        root.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
}
