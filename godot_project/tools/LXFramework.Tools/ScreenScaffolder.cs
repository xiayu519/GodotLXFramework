namespace LXFramework.Tools;

internal static class ScreenScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !args[0].Equals("screen", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: lx create screen <ClassName> [snake_case_id]");
            return 2;
        }

        var className = args[1].EndsWith("Screen", StringComparison.Ordinal) ? args[1] : args[1] + "Screen";
        CodeNames.RequireIdentifier(className, nameof(args));

        var id = args.Count >= 3 ? args[2] : CodeNames.ToSnakeCase(className[..^"Screen".Length]);
        CodeNames.RequireSnakeCase(id, nameof(args));

        var gameManifest = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        if (string.IsNullOrWhiteSpace(gameManifest.Name))
        {
            throw new InvalidOperationException("Create the game product before adding game screens.");
        }

        var manifestPath = Path.Combine(root, "content", "ui", "ui-manifest.json");
        var manifest = ToolFiles.ReadJson<UIManifest>(manifestPath);
        if (manifest.Screens.Any(screen => screen.Id == id))
        {
            throw new InvalidOperationException($"UI ID '{id}' already exists in the manifest.");
        }
        if (manifest.Screens.Any(screen => screen.ClassName == className))
        {
            throw new InvalidOperationException($"UI class '{className}' already exists in the manifest.");
        }

        var namespaceName = $"{gameManifest.RootNamespace}.UI";
        var sourcePath = ProductLayout.GetSourcePath(root, gameManifest, "UI", className + ".cs");
        var scenePath = Path.Combine(root, "scene", "ui", id + ".tscn");
        if (File.Exists(sourcePath) || File.Exists(scenePath))
        {
            throw new IOException("The requested screen source or scene already exists.");
        }

        var resourceScriptPath = ProductLayout.GetResourcePath(gameManifest, "UI", className + ".cs");
        ToolFiles.WriteText(sourcePath, BuildSource(namespaceName, className));
        ToolFiles.WriteText(scenePath, BuildScene(className, resourceScriptPath));

        manifest.Screens.Add(new UIManifestEntry
        {
            Scope = ManifestScopes.Product,
            Id = id,
            ClassName = className,
            Namespace = namespaceName,
            ScenePath = $"res://scene/ui/{id}.tscn",
            Layer = "Screen",
            CachePolicy = "Transient",
        });
        manifest.Screens = manifest.Screens.OrderBy(screen => screen.Id, StringComparer.Ordinal).ToList();
        ToolFiles.WriteJson(manifestPath, manifest);
        ProjectGenerator.Run(root);
        Console.WriteLine($"created UI '{id}' ({className})");
        return 0;
    }

    private static string BuildSource(string namespaceName, string className) =>
        $$"""
        using LX.UI;

        namespace {{namespaceName}};

        public partial class {{className}} : UIScreen
        {
            protected internal override ValueTask OnShowAsync(object? payload, CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }
        }
        """ + "\n";

    private static string BuildScene(string className, string scriptPath) =>
        $$"""
        [gd_scene load_steps=2 format=3]

        [ext_resource type="Script" path="{{scriptPath}}" id="1_screen"]

        [node name="{{className}}" type="Control"]
        layout_mode = 3
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        script = ExtResource("1_screen")

        [node name="TitleLabel" type="Label" parent="."]
        unique_name_in_owner = true
        layout_mode = 0
        offset_right = 320.0
        offset_bottom = 48.0
        text = "{{className}}"
        """ + "\n";

}
