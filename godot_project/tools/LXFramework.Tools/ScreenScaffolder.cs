namespace LXFramework.Tools;

internal static class ScreenScaffolder
{
    public static int Run(string root, IReadOnlyList<string> args)
    {
        var isPopup = args.Count > 0 && args[0].Equals("popup", StringComparison.OrdinalIgnoreCase);
        if (args.Count < 2 ||
            (!args[0].Equals("screen", StringComparison.OrdinalIgnoreCase) && !isPopup))
        {
            Console.Error.WriteLine("Usage: lx create screen|popup <ClassName> [snake_case_id]");
            return 2;
        }

        var suffix = isPopup ? "Popup" : "Screen";
        var className = args[1].EndsWith(suffix, StringComparison.Ordinal) ? args[1] : args[1] + suffix;
        CodeNames.RequireIdentifier(className, nameof(args));

        var id = args.Count >= 3 ? args[2] : CodeNames.ToSnakeCase(className[..^suffix.Length]);
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
        ToolFiles.WriteText(sourcePath, BuildSource(namespaceName, className, isPopup));
        ToolFiles.WriteText(scenePath, BuildScene(className, resourceScriptPath, isPopup));

        manifest.Screens.Add(new UIManifestEntry
        {
            Scope = ManifestScopes.Product,
            Id = id,
            ClassName = className,
            Namespace = namespaceName,
            ScenePath = $"res://scene/ui/{id}.tscn",
            Layer = isPopup ? "Popup" : "Screen",
            CachePolicy = "Transient",
            InputPolicy = isPopup ? "Modal" : "Normal",
            FocusPolicy = isPopup ? "GrabFirst" : "Preserve",
        });
        manifest.Screens = manifest.Screens.OrderBy(screen => screen.Id, StringComparer.Ordinal).ToList();
        ToolFiles.WriteJson(manifestPath, manifest);
        ProjectGenerator.Run(root);
        Console.WriteLine($"created {(isPopup ? "popup" : "UI")} '{id}' ({className})");
        return 0;
    }

    private static string BuildSource(string namespaceName, string className, bool isPopup) =>
        $$"""
        using LX.UI;

        namespace {{namespaceName}};

        public partial class {{className}} : {{(isPopup ? "UIPopupScreen" : "UIScreen")}}
        {
            protected internal override ValueTask OnShowAsync(object? payload, CancellationToken cancellationToken)
            {
                return ValueTask.CompletedTask;
            }
        }
        """ + "\n";

    private static string BuildScene(string className, string scriptPath, bool isPopup) =>
        isPopup ? BuildPopupScene(className, scriptPath) : BuildScreenScene(className, scriptPath);

    private static string BuildScreenScene(string className, string scriptPath) =>
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

        [node name="FullLayer" type="Control" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 2

        [node name="TopLayer" type="MarginContainer" parent="."]
        layout_mode = 1
        anchors_preset = 10
        anchor_right = 1.0
        grow_horizontal = 2
        mouse_filter = 2
        theme_override_constants/margin_left = 24
        theme_override_constants/margin_top = 24
        theme_override_constants/margin_right = 24

        [node name="TitleLabel" type="Label" parent="TopLayer"]
        unique_name_in_owner = true
        layout_mode = 2
        text = "{{className}}"
        horizontal_alignment = 1

        [node name="MidLayer" type="CenterContainer" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 2

        [node name="BottomLayer" type="MarginContainer" parent="."]
        layout_mode = 1
        anchors_preset = 12
        anchor_top = 1.0
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 0
        mouse_filter = 2
        theme_override_constants/margin_left = 24
        theme_override_constants/margin_right = 24
        theme_override_constants/margin_bottom = 24

        [node name="Overlay" type="Control" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 2
        """ + "\n";

    private static string BuildPopupScene(string className, string scriptPath) =>
        $$"""
        [gd_scene load_steps=2 format=3]

        [ext_resource type="Script" path="{{scriptPath}}" id="1_popup"]

        [node name="{{className}}" type="Control"]
        layout_mode = 3
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 0
        script = ExtResource("1_popup")

        [node name="Scrim" type="ColorRect" parent="."]
        unique_name_in_owner = true
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 0
        color = Color(0, 0, 0, 0.55)

        [node name="MidLayer" type="CenterContainer" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        mouse_filter = 2

        [node name="MotionPivot" type="PanelContainer" parent="MidLayer"]
        unique_name_in_owner = true
        custom_minimum_size = Vector2(420, 240)
        layout_mode = 2

        [node name="Margin" type="MarginContainer" parent="MidLayer/MotionPivot"]
        layout_mode = 2
        theme_override_constants/margin_left = 32
        theme_override_constants/margin_top = 28
        theme_override_constants/margin_right = 32
        theme_override_constants/margin_bottom = 28

        [node name="Content" type="VBoxContainer" parent="MidLayer/MotionPivot/Margin"]
        layout_mode = 2
        theme_override_constants/separation = 16

        [node name="TitleLabel" type="Label" parent="MidLayer/MotionPivot/Margin/Content"]
        unique_name_in_owner = true
        layout_mode = 2
        text = "{{className}}"
        horizontal_alignment = 1
        """ + "\n";

}
