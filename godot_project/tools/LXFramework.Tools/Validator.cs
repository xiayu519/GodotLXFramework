namespace LXFramework.Tools;

internal static class Validator
{
    public static int Run(string root)
    {
        var errors = new List<string>();
        ValidateProject(root, errors);
        ValidateJson(root, errors);
        ValidateLuban(root, errors);
        ValidateUi(root, errors);
        ValidateResources(root, errors);
        ValidateRegistrations(root, errors);
        ValidateArchitecture(root, errors);
        ValidatePublicApiDocumentation(root, errors);
        ValidateBrandAndApi(root, errors);
        ValidateHumanTooling(root, errors);
        ValidateGenerated(root, errors);

        var report = new ValidationReport(DateTimeOffset.UtcNow, errors.Count == 0, errors);
        var output = Path.Combine(root, ".lx", "validation.json");
        ToolFiles.WriteJson(output, report);
        if (errors.Count == 0)
        {
            Console.WriteLine($"LXFramework static validation passed -> {ToolFiles.Relative(root, output)}");
            return 0;
        }

        foreach (var error in errors)
        {
            Console.Error.WriteLine($"validation: {error}");
        }

        return 1;
    }

    private static void ValidateProject(string root, ICollection<string> errors)
    {
        var requiredFiles = new[]
        {
            ".editorconfig",
            "Directory.Build.props",
            "project.godot",
            "LXFramework.csproj",
            "LXFramework.sln",
            "export_presets.cfg",
            "addons/lx_tools/plugin.cfg",
            "addons/lx_tools/LXToolsPlugin.cs",
            "addons/lx_tools/run-command.ps1",
            "addons/lx_tools/invoke-command.ps1",
            "scene/main.tscn",
            "scene/ui/examples/ui_components_showcase.tscn",
            "tests/Visual/Baselines/ui_components.png",
            "content/data/content-manifest.json",
            "content/features/feature-manifest.json",
            "content/game/game-manifest.json",
            "content/input/input-manifest.json",
            "content/ui/ui-manifest.json",
            "content/res/res-manifest.json",
        };
        foreach (var relative in requiredFiles)
        {
            if (!File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
            {
                errors.Add($"Required framework file '{relative}' is missing.");
            }
        }

        var buildPropsPath = Path.Combine(root, "Directory.Build.props");
        if (File.Exists(buildPropsPath) &&
            !File.ReadAllText(buildPropsPath).Contains("<LangVersion>12.0</LangVersion>", StringComparison.Ordinal))
        {
            errors.Add("Directory.Build.props must pin C# 12.0 for the Godot 4.6 / .NET 8 baseline.");
        }

        var editorConfigPath = Path.Combine(root, ".editorconfig");
        if (File.Exists(editorConfigPath))
        {
            var editorConfig = File.ReadAllText(editorConfigPath);
            if (!editorConfig.Contains("end_of_line = lf", StringComparison.Ordinal) ||
                !editorConfig.Contains("indent_size = 4", StringComparison.Ordinal))
            {
                errors.Add(".editorconfig must retain Godot C# line-ending and indentation conventions.");
            }
        }

        var projectPath = Path.Combine(root, "project.godot");
        var mainPath = Path.Combine(root, "scene", "main.tscn");
        if (!File.Exists(projectPath) || !File.Exists(mainPath))
        {
            return;
        }

        var project = File.ReadAllText(projectPath);
        var main = File.ReadAllText(mainPath);
        const string mainUid = "uid://s543p8mkoql5";
        if (!project.Contains($"run/main_scene=\"{mainUid}\"", StringComparison.Ordinal))
        {
            errors.Add("project.godot must use scene/main.tscn as the main entry.");
        }
        if (!main.Contains($"uid=\"{mainUid}\"", StringComparison.Ordinal))
        {
            errors.Add("scene/main.tscn must retain the configured main-scene UID.");
        }
        if (!project.Contains("res://addons/lx_tools/plugin.cfg", StringComparison.Ordinal))
        {
            errors.Add("project.godot must enable the LX Tools editor plugin.");
        }
    }

    private static void ValidateJson(string root, ICollection<string> errors)
    {
        var contentRoot = Path.Combine(root, "content");
        if (!Directory.Exists(contentRoot))
        {
            errors.Add("content directory is missing.");
            return;
        }

        foreach (var path in Directory.EnumerateFiles(contentRoot, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                using var _ = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            }
            catch (System.Text.Json.JsonException exception)
            {
                errors.Add(
                    $"JSON '{ToolFiles.Relative(root, path)}' is invalid at line {exception.LineNumber}, byte {exception.BytePositionInLine}.");
            }
        }
    }

    private static void ValidateLuban(string root, ICollection<string> errors)
    {
        var workspaceRoot = Directory.GetParent(root)?.FullName ??
                            throw new InvalidDataException("Godot project must have a workspace parent.");
        var designRoot = Path.Combine(workspaceRoot, "game_design");
        var requiredFiles = new[]
        {
            "AGENTS.md",
            "README.md",
            "build.bat",
            "build.ps1",
            "install-luban.ps1",
            "luban.conf",
            "toolchain.json",
        };
        foreach (var relative in requiredFiles)
        {
            if (!File.Exists(Path.Combine(designRoot, relative)))
            {
                errors.Add($"Luban design file 'game_design/{relative}' is missing.");
            }
        }
        if (!Directory.Exists(Path.Combine(designRoot, "schema")) ||
            !Directory.EnumerateFiles(Path.Combine(designRoot, "schema"), "*.xml", SearchOption.AllDirectories).Any())
        {
            errors.Add("game_design/schema must contain at least one Luban XML schema.");
        }

        var toolchainPath = Path.Combine(designRoot, "toolchain.json");
        if (!File.Exists(toolchainPath))
        {
            return;
        }

        var toolchain = ToolFiles.ReadJson<LubanToolchain>(toolchainPath);
        if (toolchain.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(toolchain.Version) ||
            toolchain.Commit.Length != 40 ||
            !string.Equals(
                toolchain.Repository,
                "https://github.com/focus-creative-games/luban.git",
                StringComparison.Ordinal))
        {
            errors.Add("game_design/toolchain.json must pin an official Luban version and 40-character commit.");
        }

        var reportPath = Path.Combine(root, ".lx", "luban", "report.json");
        if (!File.Exists(reportPath))
        {
            errors.Add("Luban build report is missing; run lx data.");
            return;
        }
        var report = ToolFiles.ReadJson<LubanBuildReport>(reportPath);
        if (!report.Success ||
            !string.Equals(report.ToolVersion, toolchain.Version, StringComparison.Ordinal) ||
            !string.Equals(report.ToolCommit, toolchain.Commit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(report.DataFormat, "bin", StringComparison.Ordinal) ||
            !string.Equals(report.FileExtension, ".bytes", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(report.OutputHash) ||
            !report.GeneratedCodeCompiled ||
            !report.NegativeReferenceRejected)
        {
            errors.Add("Luban build report does not match the pinned toolchain or verification contract.");
        }

        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        if (string.IsNullOrWhiteSpace(game.Name))
        {
            return;
        }

        var generatedCodeRoot = Path.Combine(ProductLayout.GetGeneratedDirectory(root, game), "Luban");
        if (!Directory.Exists(generatedCodeRoot) ||
            !Directory.EnumerateFiles(generatedCodeRoot, "*.cs", SearchOption.AllDirectories).Any())
        {
            errors.Add("Luban generated C# output is missing; run lx data.");
        }
        else
        {
            foreach (var path in Directory.EnumerateFiles(generatedCodeRoot, "*.cs", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(path);
                if (!content.StartsWith("// <auto-generated by Luban ", StringComparison.Ordinal) ||
                    !content.Contains("#nullable disable", StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Luban generated file '{ToolFiles.Relative(root, path)}' is missing its deterministic header.");
                }
            }
        }

        var outputManifestPath = Path.Combine(root, "content", "data", "luban", "luban-manifest.json");
        if (!File.Exists(outputManifestPath))
        {
            errors.Add("Luban data manifest is missing; run lx data.");
            return;
        }
        var outputManifest = ToolFiles.ReadJson<LubanOutputManifest>(outputManifestPath);
        if (outputManifest.SchemaVersion != 1 ||
            !string.Equals(outputManifest.ToolVersion, toolchain.Version, StringComparison.Ordinal) ||
            !string.Equals(outputManifest.ToolCommit, toolchain.Commit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(outputManifest.DataFormat, "bin", StringComparison.Ordinal) ||
            !string.Equals(outputManifest.FileExtension, ".bytes", StringComparison.Ordinal) ||
            outputManifest.Files.Count == 0)
        {
            errors.Add("Luban data manifest does not match the pinned toolchain or contains no tables.");
        }
        foreach (var relative in outputManifest.Files)
        {
            if (Path.IsPathRooted(relative) ||
                relative.Contains("..", StringComparison.Ordinal) ||
                !relative.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Luban data manifest contains unsafe path '{relative}'.");
                continue;
            }
            if (!File.Exists(Path.Combine(root, "content", "data", "luban", relative)))
            {
                errors.Add($"Luban generated data file '{relative}' is missing.");
            }
        }
    }

    private static void ValidateUi(string root, ICollection<string> errors)
    {
        var path = Path.Combine(root, "content", "ui", "ui-manifest.json");
        if (!File.Exists(path))
        {
            return;
        }

        var manifest = ToolFiles.ReadJson<UIManifest>(path);
        foreach (var duplicate in manifest.Screens
                     .GroupBy(screen => screen.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"UI ID '{duplicate.Key}' is duplicated.");
        }
        foreach (var screen in manifest.Screens)
        {
            var scenePath = ToolFiles.ToAbsolutePath(root, screen.ScenePath);
            if (!File.Exists(scenePath))
            {
                errors.Add($"UI scene '{screen.ScenePath}' is missing.");
                continue;
            }

            var expectedScriptSuffix = $"/{screen.ClassName}.cs\"";
            if (!File.ReadAllText(scenePath).Contains(expectedScriptSuffix, StringComparison.Ordinal))
            {
                errors.Add(
                    $"UI scene '{screen.ScenePath}' does not reference script class '{screen.ClassName}'.");
            }
        }
    }

    private static void ValidateArchitecture(string root, ICollection<string> errors)
    {
        var gameManifestPath = Path.Combine(root, "content", "game", "game-manifest.json");
        var gameManifest = ToolFiles.ReadJson<GameManifest>(gameManifestPath);
        string? productRoot = null;
        if (!string.IsNullOrWhiteSpace(gameManifest.Name))
        {
            try
            {
                productRoot = ProductLayout.GetSourceDirectory(root, gameManifest);
            }
            catch (InvalidDataException exception)
            {
                errors.Add(exception.Message);
            }
        }
        var coreRoot = Path.Combine(root, "src", "LXFramework.Core");
        foreach (var path in EnumerateSourceFiles(coreRoot))
        {
            var content = File.ReadAllText(path);
            AddArchitectureDiagnostics(
                root, path, content, ArchitectureLayer.Core, gameManifest.RootNamespace, errors);
        }

        var uidRoots = new List<string>
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "tools"),
            Path.Combine(root, "tests"),
        };
        if (productRoot is not null)
        {
            uidRoots.Add(productRoot);
        }
        foreach (var uidPath in uidRoots
                     .Where(Directory.Exists)
                     .SelectMany(path => Directory.EnumerateFiles(path, "*.uid", SearchOption.AllDirectories))
                     .Where(path => !path.Split(Path.DirectorySeparatorChar)
                         .Any(segment => segment is "bin" or "obj")))
        {
            var sourcePath = uidPath[..^".uid".Length];
            if (!File.Exists(sourcePath))
            {
                errors.Add($"Orphan Godot UID sidecar '{ToolFiles.Relative(root, uidPath)}'.");
            }
        }

        var frameworkRoot = Path.Combine(root, "src", "LXFramework");
        foreach (var path in EnumerateSourceFiles(frameworkRoot)
                     .Where(path => !IsUnderGeneratedDirectory(path)))
        {
            var content = File.ReadAllText(path);
            AddArchitectureDiagnostics(
                root, path, content, ArchitectureLayer.Adapter, gameManifest.RootNamespace, errors);
        }

        if (productRoot is not null && Directory.Exists(productRoot))
        {
            foreach (var path in EnumerateSourceFiles(productRoot)
                         .Where(path => !IsUnderGeneratedDirectory(path)))
            {
                var content = File.ReadAllText(path);
                if (!content.Contains($"namespace {gameManifest.RootNamespace};", StringComparison.Ordinal) &&
                    !content.Contains($"namespace {gameManifest.RootNamespace}.", StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Product source '{ToolFiles.Relative(root, path)}' must use namespace " +
                        $"'{gameManifest.RootNamespace}' or one of its children.");
                }
                AddArchitectureDiagnostics(
                    root, path, content, ArchitectureLayer.Product, gameManifest.RootNamespace, errors);
            }
        }

        if (string.IsNullOrWhiteSpace(gameManifest.Name) &&
            !string.IsNullOrWhiteSpace(gameManifest.SourceRoot))
        {
            errors.Add("An empty game manifest must not declare sourceRoot.");
        }
        if (!string.IsNullOrWhiteSpace(gameManifest.Name) &&
            (productRoot is null || !Directory.Exists(productRoot)))
        {
            errors.Add(
                $"game-manifest.json declares a product but source root " +
                $"'{gameManifest.SourceRoot}' is missing.");
        }

        foreach (var world in gameManifest.Worlds)
        {
            var scenePath = ToolFiles.ToAbsolutePath(root, world.ScenePath);
            if (File.Exists(scenePath) &&
                !File.ReadAllText(scenePath).Contains($"/{world.ClassName}.cs\"", StringComparison.Ordinal))
            {
                errors.Add(
                    $"World scene '{world.ScenePath}' does not reference script class '{world.ClassName}'.");
            }
        }

        var featureManifestPath = Path.Combine(root, "content", "features", "feature-manifest.json");
        if (File.Exists(featureManifestPath))
        {
            var manifest = ToolFiles.ReadJson<FeatureManifest>(featureManifestPath);
            foreach (var feature in manifest.Features)
            {
                var scenePath = ToolFiles.ToAbsolutePath(root, feature.ScenePath);
                if (File.Exists(scenePath) &&
                    !File.ReadAllText(scenePath).Contains($"/{feature.ClassName}.cs\"", StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Feature scene '{feature.ScenePath}' does not reference script class '{feature.ClassName}'.");
                }
            }
        }
    }

    private static void AddArchitectureDiagnostics(
        string root,
        string path,
        string content,
        ArchitectureLayer layer,
        string? productNamespace,
        ICollection<string> errors)
    {
        foreach (var diagnostic in CSharpArchitectureAnalyzer.Analyze(content, layer, productNamespace))
        {
            errors.Add(
                $"[{diagnostic.Code}] {ToolFiles.Relative(root, path)}" +
                $"({diagnostic.Line},{diagnostic.Column}): {diagnostic.Message}");
        }
    }

    private static void ValidatePublicApiDocumentation(string root, ICollection<string> errors)
    {
        foreach (var sourceRoot in new[]
                 {
                     Path.Combine(root, "src", "LXFramework.Core"),
                     Path.Combine(root, "src", "LXFramework"),
                 })
        {
            foreach (var path in EnumerateSourceFiles(sourceRoot)
                         .Where(path => !IsUnderGeneratedDirectory(path)))
            {
                foreach (var diagnostic in PublicApiDocumentationAnalyzer.Analyze(File.ReadAllText(path)))
                {
                    errors.Add(
                        $"[LX_DOC_001] {ToolFiles.Relative(root, path)}" +
                        $"({diagnostic.Line},{diagnostic.Column}): {diagnostic.Message}");
                }
            }
        }
    }

    private static void ValidateHumanTooling(string root, ICollection<string> errors)
    {
        var workspaceRoot = Directory.GetParent(root)?.FullName ?? root;
        var readmePath = Path.Combine(workspaceRoot, "README.md");
        if (!File.Exists(readmePath))
        {
            errors.Add("Workspace README.md is missing.");
            return;
        }

        var readme = File.ReadAllText(readmePath);
        foreach (var term in new[]
                 {
                     "LX Tools",
                     ".\\lx.ps1 create",
                     ".\\lx.ps1 data",
                     ".\\lx.ps1 visual compare",
                     ".\\lx.ps1 export windows",
                     "game_design/schema",
                 })
        {
            if (!readme.Contains(term, StringComparison.Ordinal))
            {
                errors.Add($"README.md must document the human workflow term '{term}'.");
            }
        }

        var editorPluginPath = Path.Combine(root, "addons", "lx_tools", "LXToolsPlugin.cs");
        if (File.Exists(editorPluginPath))
        {
            var editorPlugin = File.ReadAllText(editorPluginPath);
            foreach (var label in new[]
                     {
                         "创建内容…",
                         "生成策划数据",
                         "场景依赖",
                         "打开策划数据目录",
                     })
            {
                if (!editorPlugin.Contains($"\"{label}\"", StringComparison.Ordinal))
                {
                    errors.Add($"LX Tools must expose the Chinese developer action '{label}'.");
                }
            }
            foreach (var maintenanceLabel in new[]
                     {
                         "Validate",
                         "Generate Bindings",
                         "Luban Data",
                         "Create…",
                         "Visual Compare",
                         "Visual Approve",
                     })
            {
                if (editorPlugin.Contains($"\"{maintenanceLabel}\"", StringComparison.Ordinal))
                {
                    errors.Add(
                        $"LX Tools must keep maintenance action '{maintenanceLabel}' out of the developer toolbar.");
                }
            }
        }

        var presetPath = Path.Combine(root, "export_presets.cfg");
        if (File.Exists(presetPath) &&
            !File.ReadAllText(presetPath).Contains("name=\"Windows Desktop\"", StringComparison.Ordinal))
        {
            errors.Add("export_presets.cfg must contain the Windows Desktop release preset.");
        }
    }

    private static void ValidateResources(string root, ICollection<string> errors)
    {
        var path = Path.Combine(root, "content", "res", "res-manifest.json");
        if (!File.Exists(path))
        {
            return;
        }

        var manifest = ToolFiles.ReadJson<AssetManifest>(path);
        foreach (var duplicate in manifest.Assets
                     .GroupBy(asset => asset.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Resource ID '{duplicate.Key}' is duplicated.");
        }
        foreach (var asset in manifest.Assets)
        {
            var assetPath = ToolFiles.ToAbsolutePath(root, asset.Path);
            if (!File.Exists(assetPath))
            {
                errors.Add($"Resource '{asset.Path}' is missing.");
            }
        }
    }

    private static void ValidateRegistrations(string root, ICollection<string> errors)
    {
        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        ValidateRegisteredFiles(
            root,
            Path.Combine(root, "scene", "world"),
            "*.tscn",
            game.Worlds.Select(world => world.ScenePath),
            "world scene",
            errors);

        var features = ToolFiles.ReadJson<FeatureManifest>(
            Path.Combine(root, "content", "features", "feature-manifest.json"));
        ValidateRegisteredFiles(
            root,
            Path.Combine(root, "scene", "features"),
            "*.tscn",
            features.Features.Select(feature => feature.ScenePath),
            "feature scene",
            errors);

        var ui = ToolFiles.ReadJson<UIManifest>(
            Path.Combine(root, "content", "ui", "ui-manifest.json"));
        ValidateRegisteredFiles(
            root,
            Path.Combine(root, "scene", "ui"),
            "*.tscn",
            ui.Screens.Select(screen => screen.ScenePath),
            "UI scene",
            errors);

        var content = ToolFiles.ReadJson<ContentManifest>(
            Path.Combine(root, "content", "data", "content-manifest.json"));
        var registeredContentPaths = content.Tables.Select(table => table.Path).ToList();
        var lubanManifestPath = Path.Combine(root, "content", "data", "luban", "luban-manifest.json");
        if (File.Exists(lubanManifestPath))
        {
            var lubanManifest = ToolFiles.ReadJson<LubanOutputManifest>(lubanManifestPath);
            registeredContentPaths.AddRange(lubanManifest.Files.Select(
                file => $"res://content/data/luban/{file.Replace('\\', '/')}"));
        }
        ValidateRegisteredFiles(
            root,
            Path.Combine(root, "content", "data"),
            "*.json",
            registeredContentPaths,
            "content table",
            errors,
            ignoredFileNames: new HashSet<string>(
                ["content-manifest.json", "luban-manifest.json"],
                StringComparer.OrdinalIgnoreCase));
    }

    private static void ValidateRegisteredFiles(
        string root,
        string directory,
        string pattern,
        IEnumerable<string> registeredResourcePaths,
        string kind,
        ICollection<string> errors,
        IReadOnlySet<string>? ignoredFileNames = null)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var registered = registeredResourcePaths
            .Select(path => Path.GetFullPath(ToolFiles.ToAbsolutePath(root, path)))
            .ToHashSet(comparer);
        foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
        {
            if (ignoredFileNames?.Contains(Path.GetFileName(path)) == true)
            {
                continue;
            }
            if (!registered.Contains(Path.GetFullPath(path)))
            {
                errors.Add($"Unregistered {kind} '{ToolFiles.Relative(root, path)}'.");
            }
        }
    }

    private static void ValidateGenerated(string root, ICollection<string> errors)
    {
        IReadOnlyDictionary<string, string> expected;
        try
        {
            expected = ProjectGenerator.BuildOutputs(root);
        }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
            return;
        }

        foreach (var output in expected)
        {
            if (!File.Exists(output.Key))
            {
                errors.Add($"Generated file '{ToolFiles.Relative(root, output.Key)}' is missing.");
                continue;
            }

            var actual = File.ReadAllText(output.Key).Replace("\r\n", "\n", StringComparison.Ordinal);
            var normalizedExpected = output.Value.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!string.Equals(actual, normalizedExpected, StringComparison.Ordinal))
            {
                errors.Add($"Generated file '{ToolFiles.Relative(root, output.Key)}' is stale; run lx generate.");
            }
        }


        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var expectedPaths = expected.Keys.Select(Path.GetFullPath).ToHashSet(comparer);
        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        var generatedRoots = new List<string>
        {
            Path.Combine(root, "src", "LXFramework", "Generated"),
        };
        if (!string.IsNullOrWhiteSpace(game.Name))
        {
            generatedRoots.Add(ProductLayout.GetGeneratedDirectory(root, game));
        }
        foreach (var generatedRoot in generatedRoots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(generatedRoot, "*.g.cs", SearchOption.AllDirectories))
            {
                if (!expectedPaths.Contains(Path.GetFullPath(path)))
                {
                    errors.Add(
                        $"Generated file '{ToolFiles.Relative(root, path)}' has no source-of-truth output; run lx generate.");
                }
                if (!File.ReadAllText(path)
                        .StartsWith("// <auto-generated by LXFramework.Tools />", StringComparison.Ordinal))
                {
                    errors.Add($"Generated file '{ToolFiles.Relative(root, path)}' is missing its generated header.");
                }
            }
        }
    }

    private static void ValidateBrandAndApi(string root, ICollection<string> errors)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".godot", ".mono", ".tools", ".lx", ".peach" + "wind",
            "bin", "obj", "artifacts", "research",
        };
        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".sln", ".ps1", ".md", ".json", ".godot", ".tscn", ".tres",
        };
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(path => textExtensions.Contains(Path.GetExtension(path)))
                     .Where(path => !ToolFiles.Relative(root, path).Split('/').Any(excluded.Contains)))
        {
            var content = File.ReadAllText(path);
            var legacyTitle = "Peach" + "wind";
            var legacyLower = "peach" + "wind";
            if (content.Contains(legacyTitle, StringComparison.Ordinal) ||
                content.Contains(legacyLower, StringComparison.Ordinal))
            {
                errors.Add($"Legacy framework name remains in '{ToolFiles.Relative(root, path)}'.");
            }
        }

        var contextPath = Path.Combine(root, "src", "LXFramework", "Runtime", "LXContext.cs");
        var nodePath = Path.Combine(root, "src", "LXFramework", "Runtime", "LXNode.cs");
        var uiScreenPath = Path.Combine(root, "src", "LXFramework", "UI", "UIScreen.cs");
        var uiServicePath = Path.Combine(root, "src", "LXFramework", "UI", "UIService.cs");
        if (!File.Exists(contextPath) ||
            !File.ReadAllText(contextPath).Contains("AssetRegistry Res,", StringComparison.Ordinal) ||
            !File.ReadAllText(contextPath).Contains("UIService UI,", StringComparison.Ordinal) ||
            !File.ReadAllText(contextPath).Contains("WorldEventJournal WorldEvents,", StringComparison.Ordinal))
        {
            errors.Add("LXContext must expose LX.Res, LX.UI, and persistent world events.");
        }
        foreach (var path in new[] { nodePath, uiScreenPath })
        {
            if (!File.Exists(path) ||
                !File.ReadAllText(path).Contains("protected LXContext LX", StringComparison.Ordinal))
            {
                errors.Add($"Context-aware base '{ToolFiles.Relative(root, path)}' must expose the injected LX call root.");
            }
        }

        if (!File.Exists(uiServicePath) ||
            !File.ReadAllText(uiServicePath).Contains("Layer = 100", StringComparison.Ordinal) ||
            !File.ReadAllText(uiServicePath).Contains("FollowViewportEnabled = false", StringComparison.Ordinal))
        {
            errors.Add("LX.UI must render in a fixed CanvasLayer above the default 2D world canvas.");
        }

        if (!Directory.Exists(Path.Combine(root, "src", "LXFramework", "Res")) ||
            Directory.Exists(Path.Combine(root, "src", "LXFramework", "Assets")) ||
            File.Exists(Path.Combine(root, "peach" + "wind.ps1")))
        {
            errors.Add("LXFramework resource module or root tool naming is inconsistent.");
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"));
    }

    private static bool IsUnderGeneratedDirectory(string path) =>
        path.Split(Path.DirectorySeparatorChar)
            .Contains("Generated", StringComparer.OrdinalIgnoreCase);
}

internal sealed record ValidationReport(
    DateTimeOffset ValidatedAtUtc,
    bool Success,
    IReadOnlyList<string> Errors);

internal sealed record LubanToolchain(
    int SchemaVersion,
    string Version,
    string Commit,
    string Repository,
    string Assembly);

internal sealed record LubanBuildReport(
    DateTimeOffset ValidatedAtUtc,
    bool Success,
    string ToolVersion,
    string ToolCommit,
    string ToolAssembly,
    string DataFormat,
    string FileExtension,
    string InputHash,
    string? Product,
    string CodeOutput,
    string DataOutput,
    IReadOnlyList<string> CodeFiles,
    IReadOnlyList<string> DataFiles,
    string OutputHash,
    bool GeneratedCodeCompiled,
    bool NegativeReferenceRejected);

internal sealed record LubanOutputManifest(
    int SchemaVersion,
    string Generator,
    string ToolVersion,
    string ToolCommit,
    string DataFormat,
    string FileExtension,
    string InputHash,
    IReadOnlyList<string> Files);
