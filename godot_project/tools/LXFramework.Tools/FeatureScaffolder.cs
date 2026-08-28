namespace LXFramework.Tools;

internal static class FeatureScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: lx create feature <Name> [snake_case_id]");
            return 2;
        }

        var className = args[1].EndsWith("Feature", StringComparison.Ordinal)
            ? args[1]
            : args[1] + "Feature";
        CodeNames.RequireIdentifier(className, nameof(args));
        var baseName = className[..^"Feature".Length];
        var id = args.Count >= 3 ? args[2] : CodeNames.ToSnakeCase(baseName);
        CodeNames.RequireSnakeCase(id, nameof(args));

        var gameManifest = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        if (string.IsNullOrWhiteSpace(gameManifest.Name))
        {
            throw new InvalidOperationException("Create the game product before adding features.");
        }

        var manifestPath = Path.Combine(root, "content", "features", "feature-manifest.json");
        var manifest = ToolFiles.ReadJson<FeatureManifest>(manifestPath);
        if (manifest.Features.Any(feature => string.Equals(feature.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Feature ID '{id}' already exists.");
        }

        var namespaceName = $"{gameManifest.RootNamespace}.Features.{baseName}";
        var sourcePath = ProductLayout.GetSourcePath(
            root,
            gameManifest,
            "Features",
            baseName,
            className + ".cs");
        var scenePath = Path.Combine(root, "scene", "features", id + ".tscn");
        if (File.Exists(sourcePath) || File.Exists(scenePath))
        {
            throw new IOException("The requested feature source or scene already exists.");
        }

        ToolFiles.WriteText(sourcePath,
            $$"""
            using LX.Runtime;

            namespace {{namespaceName}};

            public partial class {{className}} : LXNode
            {
                protected override void OnLXInitialized()
                {
                }
            }
            """ + "\n");
        var resourceScriptPath = ProductLayout.GetResourcePath(
            gameManifest,
            "Features",
            baseName,
            className + ".cs");
        ToolFiles.WriteText(scenePath,
            $$"""
            [gd_scene load_steps=2 format=3]

            [ext_resource type="Script" path="{{resourceScriptPath}}" id="1_feature"]

            [node name="{{className}}" type="Node"]
            script = ExtResource("1_feature")
            """ + "\n");
        manifest.Features.Add(new FeatureManifestEntry
        {
            Scope = ManifestScopes.Product,
            Id = id,
            ClassName = className,
            Namespace = namespaceName,
            ScenePath = $"res://scene/features/{id}.tscn",
        });
        manifest.Features = manifest.Features.OrderBy(feature => feature.Id, StringComparer.Ordinal).ToList();
        ToolFiles.WriteJson(manifestPath, manifest);
        ProjectGenerator.Run(root);
        Console.WriteLine($"created feature '{id}' ({className})");
        return 0;
    }
}
