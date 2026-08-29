namespace LXFramework.Tools;

internal static class ResScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count < 4 || args.Count > 6)
        {
            Console.Error.WriteLine(
                "Usage: lx create res <snake_case_id> <ResourceType> <res://path> " +
                "[Transient|Cached|Resident] [snake_case_group]");
            return 2;
        }

        var id = args[1].Trim();
        var resourceType = args[2].Trim();
        var resourcePath = args[3].Trim();
        var policy = args.Count >= 5 ? args[4].Trim() : "Cached";
        var group = args.Count >= 6 ? args[5].Trim() : null;
        CodeNames.RequireSnakeCase(id, nameof(args));
        foreach (var typeSegment in resourceType.Split('.'))
        {
            CodeNames.RequireIdentifier(typeSegment, nameof(args));
        }
        if (policy is not ("Transient" or "Cached" or "Resident"))
        {
            throw new ArgumentException($"Unsupported resource cache policy '{policy}'.", nameof(args));
        }
        if (group is not null)
        {
            CodeNames.RequireSnakeCase(group, nameof(args));
        }
        if (!File.Exists(ToolFiles.ToAbsolutePath(root, resourcePath)))
        {
            throw new FileNotFoundException($"Resource '{resourcePath}' does not exist.");
        }

        var manifestPath = Path.Combine(root, "content", "res", "res-manifest.json");
        var manifest = ToolFiles.ReadJson<AssetManifest>(manifestPath);
        if (manifest.Assets.Any(asset => string.Equals(asset.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Resource ID '{id}' already exists.");
        }

        manifest.Assets.Add(new AssetManifestEntry
        {
            Scope = ManifestScopes.Product,
            Id = id,
            ResourceType = resourceType,
            Path = resourcePath,
            CachePolicy = policy,
            Group = group,
        });
        manifest.Assets = manifest.Assets.OrderBy(asset => asset.Id, StringComparer.Ordinal).ToList();
        ToolFiles.WriteJson(manifestPath, manifest);
        ProjectGenerator.Run(root);
        Console.WriteLine($"registered resource '{id}' ({resourceType})");
        Console.WriteLine(
            $"typed reference      LX.Generated.ResCatalog.{CodeNames.ToPascalCase(id)}");
        Console.WriteLine(
            "generated catalog    src/LXFramework/Generated/ResCatalog.g.cs (do not inspect or edit)");
        return 0;
    }
}
