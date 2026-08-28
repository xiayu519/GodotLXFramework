namespace LXFramework.Tools;

internal static class InputScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count < 3)
        {
            Console.Error.WriteLine(
                "Usage: lx create input <Name> <godot_action> [DefaultPhysicalKey]");
            return 2;
        }

        var id = CodeNames.ToSnakeCase(args[1]);
        CodeNames.RequireSnakeCase(id, nameof(args));
        var godotAction = args[2].Trim();
        var defaultKey = args.Count >= 4 ? args[3].Trim() : null;
        if (defaultKey is not null)
        {
            CodeNames.RequireIdentifier(defaultKey, nameof(args));
        }

        var manifestPath = Path.Combine(root, "content", "input", "input-manifest.json");
        var manifest = ToolFiles.ReadJson<InputManifest>(manifestPath);
        if (manifest.Actions.Any(action => string.Equals(action.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Input action ID '{id}' already exists.");
        }
        if (manifest.Actions.SelectMany(action => action.Routes)
            .Any(route => string.Equals(route.GodotAction, godotAction, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Godot input action '{godotAction}' is already routed.");
        }

        manifest.Actions.Add(new InputManifestAction
        {
            Scope = ManifestScopes.Product,
            Id = id,
            Routes =
            [
                new InputManifestRoute
                {
                    GodotAction = godotAction,
                    DefaultPhysicalKey = defaultKey,
                },
            ],
        });
        manifest.Actions = manifest.Actions.OrderBy(action => action.Id, StringComparer.Ordinal).ToList();
        ToolFiles.WriteJson(manifestPath, manifest);
        ProjectGenerator.Run(root);
        Console.WriteLine($"created input action '{id}' routed from '{godotAction}'");
        return 0;
    }
}
