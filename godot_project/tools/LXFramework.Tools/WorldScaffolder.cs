namespace LXFramework.Tools;

internal static class WorldScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: lx create world <Name> [snake_case_id]");
            return 2;
        }

        var className = args[1].EndsWith("World", StringComparison.Ordinal)
            ? args[1]
            : args[1] + "World";
        CodeNames.RequireIdentifier(className, nameof(args));
        var baseName = className[..^"World".Length];
        var id = args.Count >= 3 ? args[2] : CodeNames.ToSnakeCase(baseName);
        CodeNames.RequireSnakeCase(id, nameof(args));

        var manifestPath = Path.Combine(root, "content", "game", "game-manifest.json");
        var manifest = ToolFiles.ReadJson<GameManifest>(manifestPath);
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidOperationException("Create the game product before adding additional worlds.");
        }
        if (manifest.Worlds.Any(world => string.Equals(world.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"World ID '{id}' already exists.");
        }

        var namespaceName = $"{manifest.RootNamespace}.Worlds.{baseName}";
        var sourcePath = ProductLayout.GetSourcePath(root, manifest, "Worlds", baseName, className + ".cs");
        var scenePath = Path.Combine(root, "scene", "world", id + ".tscn");
        if (File.Exists(sourcePath) || File.Exists(scenePath))
        {
            throw new IOException("The requested world source or scene already exists.");
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
        var resourceScriptPath = ProductLayout.GetResourcePath(manifest, "Worlds", baseName, className + ".cs");
        ToolFiles.WriteText(scenePath,
            $$"""
            [gd_scene load_steps=2 format=3]

            [ext_resource type="Script" path="{{resourceScriptPath}}" id="1_world"]

            [node name="{{className}}" type="Node"]
            script = ExtResource("1_world")
            """ + "\n");

        manifest.Worlds.Add(new WorldManifestEntry
        {
            Id = id,
            ClassName = className,
            Namespace = namespaceName,
            ScenePath = $"res://scene/world/{id}.tscn",
        });
        manifest.Worlds = manifest.Worlds.OrderBy(world => world.Id, StringComparer.Ordinal).ToList();
        ToolFiles.WriteJson(manifestPath, manifest);
        ProjectGenerator.Run(root);
        Console.WriteLine($"created world '{id}' ({className})");
        return 0;
    }
}
