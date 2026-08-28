using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static partial class ProjectInspector
{
    private static readonly string[] ExcludedSegments =
    [
        ".git", ".godot", ".mono", ".tools", "bin", "obj", ".lx", ".peach" + "wind",
        "artifacts", "research",
    ];

    public static int Run(string root, IReadOnlyList<string>? arguments = null)
    {
        var includeFiles = arguments?.Contains("--full", StringComparer.OrdinalIgnoreCase) == true;
        if (arguments?.Any(argument => !string.Equals(argument, "--full", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new ArgumentException("inspect accepts only --full.");
        }

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(root, path))
            .Select(path => ToolFiles.Relative(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var scenes = files
            .Where(path => path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
            .Select(path => BuildSceneEntry(root, path))
            .ToArray();

        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        var ui = ToolFiles.ReadJson<UIManifest>(
            Path.Combine(root, "content", "ui", "ui-manifest.json"));
        var resources = ToolFiles.ReadJson<AssetManifest>(
            Path.Combine(root, "content", "res", "res-manifest.json"));
        var input = ToolFiles.ReadJson<InputManifest>(
            Path.Combine(root, "content", "input", "input-manifest.json"));
        var content = ToolFiles.ReadJson<ContentManifest>(
            Path.Combine(root, "content", "data", "content-manifest.json"));
        var features = ToolFiles.ReadJson<FeatureManifest>(
            Path.Combine(root, "content", "features", "feature-manifest.json"));

        var index = new ProjectIndex(
            4,
            "LXFramework",
            new ProductIndexEntry(
                game.Name,
                game.RootNamespace,
                ProductLayout.GetSourceRoot(game),
                game.InitialWorldId,
                game.Worlds.Select(world =>
                    new RegisteredEntry(world.Id, world.ScenePath, ManifestScopes.Product)).ToArray()),
            ReadContextServices(root),
            new CatalogIndex(
                ui.Screens.Select(screen =>
                    new RegisteredEntry(screen.Id, screen.ScenePath, screen.Scope)).ToArray(),
                resources.Assets.Select(asset =>
                    new RegisteredEntry(asset.Id, asset.Path, asset.Scope)).ToArray(),
                input.Actions.Select(action => new RegisteredEntry(
                    action.Id,
                    string.Join(",", action.Routes.Select(route => route.GodotAction)),
                    action.Scope)).ToArray(),
                content.Tables.Select(table =>
                    new RegisteredEntry(table.Id, table.Path, table.Scope)).ToArray(),
                features.Features.Select(feature =>
                    new RegisteredEntry(feature.Id, feature.ScenePath, feature.Scope)).ToArray()),
            ReadLuban(root),
            ReadExtensionTypes(root, files, game),
            includeFiles ? files : null,
            scenes,
            files.Count(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)),
            files.Count(path => path.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)));
        var output = Path.Combine(root, ".lx", "project-index.json");
        ToolFiles.WriteJson(output, index);
        Console.WriteLine(
            $"inspect passed ({(includeFiles ? "full" : "compact")}): " +
            $"{files.Length} files, {scenes.Length} scenes, " +
            $"{index.ContextServices.Count} services -> {ToolFiles.Relative(root, output)}");
        return 0;
    }

    private static SceneIndexEntry BuildSceneEntry(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parsed = TscnParser.Parse(path);
        var content = File.ReadAllText(path);
        var script = ScriptResourceRegex().Match(content);
        return new SceneIndexEntry(
            relativePath,
            parsed.Root.Name,
            parsed.Root.Type,
            script.Success ? script.Groups["path"].Value : null,
            parsed.Nodes.Count,
            parsed.Nodes.Where(node => node.UniqueNameInOwner).Select(node => node.Name).ToArray());
    }

    private static IReadOnlyList<ContextServiceIndexEntry> ReadContextServices(string root)
    {
        var path = Path.Combine(root, "src", "LXFramework", "Runtime", "LXContext.cs");
        var content = File.ReadAllText(path);
        var body = ContextRecordRegex().Match(content);
        if (!body.Success)
        {
            return [];
        }

        return body.Groups["body"].Value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => ContextParameterRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => new ContextServiceIndexEntry(
                match.Groups["name"].Value,
                match.Groups["type"].Value))
            .ToArray();
    }

    private static IReadOnlyList<ExtensionTypeIndexEntry> ReadExtensionTypes(
        string root,
        IEnumerable<string> files,
        GameManifest game)
    {
        var results = new List<ExtensionTypeIndexEntry>();
        var productPrefix = ProductLayout.GetSourceRoot(game);
        if (productPrefix.Length > 0)
        {
            productPrefix += "/";
        }
        foreach (var relative in files.Where(path =>
                     (path.StartsWith("src/", StringComparison.Ordinal) ||
                      productPrefix.Length > 0 && path.StartsWith(productPrefix, StringComparison.Ordinal)) &&
                                                     path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                                                     !path.Contains("/Generated/", StringComparison.Ordinal)))
        {
            var content = File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            var match = ExtensionTypeRegex().Match(content);
            if (match.Success)
            {
                results.Add(new ExtensionTypeIndexEntry(
                    match.Groups["class"].Value,
                    match.Groups["base"].Value,
                    relative));
            }
        }

        return results.OrderBy(result => result.ClassName, StringComparer.Ordinal).ToArray();
    }

    private static LubanIndexEntry ReadLuban(string root)
    {
        var workspaceRoot = Directory.GetParent(root)?.FullName ?? root;
        var designRoot = Path.Combine(workspaceRoot, "game_design");
        var toolchainPath = Path.Combine(designRoot, "toolchain.json");
        if (!File.Exists(toolchainPath))
        {
            return new LubanIndexEntry(false, "", "", [], []);
        }

        var toolchain = ToolFiles.ReadJson<LubanToolchain>(toolchainPath);
        var inputs = Directory.EnumerateFiles(designRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".xml" or ".json")
            .Select(path => $"game_design/{Path.GetRelativePath(designRoot, path).Replace('\\', '/')}")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var manifestPath = Path.Combine(root, "content", "data", "luban", "luban-manifest.json");
        var outputs = File.Exists(manifestPath)
            ? ToolFiles.ReadJson<LubanOutputManifest>(manifestPath).Files
                .Select(path => $"content/data/luban/{path}")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : [];
        return new LubanIndexEntry(true, toolchain.Version, toolchain.Commit, inputs, outputs);
    }

    private static bool IsExcluded(string root, string path)
    {
        var relative = ToolFiles.Relative(root, path);
        var segments = relative.Split('/');
        return segments.Any(segment => ExcludedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    [GeneratedRegex("\\[ext_resource\\s+type=\"Script\"\\s+path=\"(?<path>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptResourceRegex();

    [GeneratedRegex("public\\s+sealed\\s+record\\s+LXContext\\((?<body>[\\s\\S]*?)\\);", RegexOptions.CultureInvariant)]
    private static partial Regex ContextRecordRegex();

    [GeneratedRegex("^(?<type>[A-Za-z_][A-Za-z0-9_.<>]*)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*),?$", RegexOptions.CultureInvariant)]
    private static partial Regex ContextParameterRegex();

    [GeneratedRegex("class\\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\\s*:\\s*(?<base>LXNode|UIScreen)", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionTypeRegex();
}

internal sealed record ProjectIndex(
    int Version,
    string Project,
    ProductIndexEntry Product,
    IReadOnlyList<ContextServiceIndexEntry> ContextServices,
    CatalogIndex Catalogs,
    LubanIndexEntry Luban,
    IReadOnlyList<ExtensionTypeIndexEntry> ExtensionTypes,
    IReadOnlyList<string>? Files,
    IReadOnlyList<SceneIndexEntry> Scenes,
    int CSharpFileCount,
    int ResourceFileCount);

internal sealed record ProductIndexEntry(
    string Name,
    string RootNamespace,
    string SourceRoot,
    string InitialWorldId,
    IReadOnlyList<RegisteredEntry> Worlds);

internal sealed record CatalogIndex(
    IReadOnlyList<RegisteredEntry> Ui,
    IReadOnlyList<RegisteredEntry> Resources,
    IReadOnlyList<RegisteredEntry> Input,
    IReadOnlyList<RegisteredEntry> Content,
    IReadOnlyList<RegisteredEntry> Features);

internal sealed record RegisteredEntry(string Id, string Target, string Scope);

internal sealed record LubanIndexEntry(
    bool Configured,
    string ToolVersion,
    string ToolCommit,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs);

internal sealed record ContextServiceIndexEntry(string Name, string Type);

internal sealed record ExtensionTypeIndexEntry(string ClassName, string BaseType, string Path);

internal sealed record SceneIndexEntry(
    string Path,
    string RootName,
    string RootType,
    string? ScriptPath,
    int NodeCount,
    IReadOnlyList<string> UniqueNodes);
