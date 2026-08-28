namespace LXFramework.Tools;

internal static class ContentScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: lx create content <Name> [snake_case_table]");
            return 2;
        }

        var className = args[1].EndsWith("Definition", StringComparison.Ordinal)
            ? args[1]
            : args[1] + "Definition";
        CodeNames.RequireIdentifier(className, nameof(args));
        var baseName = className[..^"Definition".Length];
        var table = args.Count >= 3 ? args[2] : CodeNames.ToSnakeCase(baseName);
        CodeNames.RequireSnakeCase(table, nameof(args));

        var gameManifest = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        if (string.IsNullOrWhiteSpace(gameManifest.Name))
        {
            throw new InvalidOperationException("Create the game product before adding content tables.");
        }

        var manifestPath = Path.Combine(root, "content", "data", "content-manifest.json");
        var manifest = ToolFiles.ReadJson<ContentManifest>(manifestPath);
        if (manifest.Tables.Any(entry => string.Equals(entry.Id, table, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Content table ID '{table}' already exists.");
        }

        var namespaceName = $"{gameManifest.RootNamespace}.Content";
        var sourcePath = ProductLayout.GetSourcePath(root, gameManifest, "Content", className + ".cs");
        var dataPath = Path.Combine(root, "content", "data", table + ".json");
        if (File.Exists(sourcePath) || File.Exists(dataPath))
        {
            throw new IOException("The requested content definition or data table already exists.");
        }

        ToolFiles.WriteText(sourcePath,
            $$"""
            using LX.Core.Data;

            namespace {{namespaceName}};

            public sealed record {{className}}(string Id) : IDataRecord<string>;
            """ + "\n");
        ToolFiles.WriteText(dataPath, "[]\n");
        manifest.Tables.Add(new ContentManifestEntry
        {
            Scope = ManifestScopes.Product,
            Id = table,
            ClassName = className,
            Namespace = namespaceName,
            Path = $"res://content/data/{table}.json",
        });
        manifest.Tables = manifest.Tables.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToList();
        ToolFiles.WriteJson(manifestPath, manifest);
        ProjectGenerator.Run(root);
        Console.WriteLine($"created content table '{table}' ({className})");
        return 0;
    }
}
