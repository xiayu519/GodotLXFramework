using System.Text;
using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static partial class GameGenerator
{
    private const string ManifestRelativePath = "content/game/game-manifest.json";

    public static IReadOnlyDictionary<string, string> BuildOutputs(string root)
    {
        var manifestPath = Path.Combine(root, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Game manifest is missing.", manifestPath);
        }

        var manifest = ToolFiles.ReadJson<GameManifest>(manifestPath);
        Validate(root, manifest);
        var generated = Path.Combine(root, "src", "LXFramework", "Generated");
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.Combine(generated, "GameCatalog.g.cs")] = BuildGameCatalog(manifest),
            [Path.Combine(generated, "WorldCatalog.g.cs")] = BuildWorldCatalog(manifest),
        };
    }

    internal static void Validate(string root, GameManifest manifest)
    {
        if (manifest.Version != 1)
        {
            throw new InvalidDataException($"Unsupported game manifest version {manifest.Version}.");
        }
        if (!NamespaceRegex().IsMatch(manifest.RootNamespace))
        {
            throw new InvalidDataException($"Game root namespace '{manifest.RootNamespace}' is invalid.");
        }
        if (!string.IsNullOrWhiteSpace(manifest.Name))
        {
            _ = ProductLayout.GetSourceRoot(manifest);
        }

        var productSmokes = manifest.GetProductSmokes();
        var duplicateSmoke = productSmokes
            .GroupBy(smoke => smoke.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSmoke is not null)
        {
            throw new InvalidDataException($"Product smoke ID '{duplicateSmoke.Key}' is duplicated.");
        }
        foreach (var smoke in productSmokes)
        {
            CodeNames.RequireSnakeCase(smoke.Id, nameof(smoke.Id));
            if (smoke.Id is "framework" or "all" or "affected")
            {
                throw new InvalidDataException(
                    $"Product smoke ID '{smoke.Id}' is reserved by the smoke command.");
            }
            if (!smoke.Argument.StartsWith("--", StringComparison.Ordinal) ||
                smoke.Argument.Any(char.IsWhiteSpace))
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' must declare one '--' prefixed user argument.");
            }
            if (smoke.ScenePath is { Length: > 0 } scenePath &&
                (!scenePath.StartsWith("res://", StringComparison.Ordinal) ||
                 !scenePath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) ||
                 !File.Exists(ToolFiles.ToAbsolutePath(root, scenePath))))
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' scenePath must reference an existing res:// .tscn scene.");
            }
            if (!IsSingleLineMarker(smoke.SuccessMarker))
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' must declare a non-empty single-line successMarker.");
            }
            if (smoke.TimeoutSeconds is < 1 or > 300)
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' timeoutSeconds must be between 1 and 300.");
            }
            if (smoke.CheckPaths is null)
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' checkPaths must be an array when declared.");
            }
            ValidateCheckPatterns(smoke.CheckPaths, $"Product smoke '{smoke.Id}' checkPaths");

            if (smoke.Checkpoints is null)
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' checkpoints must be an array when declared.");
            }

            var duplicateCheckpoint = smoke.Checkpoints
                .GroupBy(checkpoint => checkpoint.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateCheckpoint is not null)
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' checkpoint ID '{duplicateCheckpoint.Key}' is duplicated.");
            }
            if (smoke.Checkpoints.Any(checkpoint => string.Equals(checkpoint.Id, smoke.Id, StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' checkpoint IDs cannot reuse the scenario ID while successMarker is declared.");
            }
            foreach (var checkpoint in smoke.Checkpoints)
            {
                CodeNames.RequireSnakeCase(checkpoint.Id, nameof(checkpoint.Id));
                if (!IsSingleLineMarker(checkpoint.SuccessMarker))
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' checkpoint '{checkpoint.Id}' must declare a single-line successMarker.");
                }
            }
            if (smoke.StatePolicy is { } statePolicy)
            {
                if (statePolicy.Compare is null || statePolicy.MetricGauges is null)
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' statePolicy arrays cannot be null.");
                }
                var supportedSections = new HashSet<string>(
                    ["resources", "ui", "features", "audio", "input", "actions"],
                    StringComparer.Ordinal);
                if (statePolicy.Compare.Any(section => !supportedSections.Contains(section)))
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' statePolicy.compare supports only: " +
                        string.Join(", ", supportedSections));
                }
                if (statePolicy.Compare.Count != statePolicy.Compare.Distinct(StringComparer.Ordinal).Count())
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' statePolicy.compare contains duplicates.");
                }
                if (statePolicy.MetricGauges.Any(string.IsNullOrWhiteSpace) ||
                    statePolicy.MetricGauges.Count != statePolicy.MetricGauges.Distinct(StringComparer.Ordinal).Count())
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' statePolicy.metricGauges must contain unique non-empty names.");
                }
            }

            if (smoke.PerformanceChecks is null)
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' performanceChecks must be an array when declared.");
            }
            var duplicatePerformanceCheck = smoke.PerformanceChecks
                .GroupBy(check => check.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicatePerformanceCheck is not null)
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' performance check ID " +
                    $"'{duplicatePerformanceCheck.Key}' is duplicated.");
            }
            foreach (var performance in smoke.PerformanceChecks)
            {
                CodeNames.RequireSnakeCase(performance.Id, nameof(performance.Id));
                if (performance.SampleSource is not ("Frames" or "PhysicsFrames"))
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' performance check '{performance.Id}' " +
                        "sampleSource must be Frames or PhysicsFrames.");
                }
                if (!double.IsFinite(performance.WindowSeconds) ||
                    performance.WindowSeconds is < 1 or > 60)
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' performance check '{performance.Id}' " +
                        "windowSeconds must be between 1 and 60.");
                }
                if (performance.MinSamples is < 1 or > 16_384)
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' performance check '{performance.Id}' " +
                        "minSamples must be between 1 and 16384.");
                }
                var durationBudgets = new[]
                {
                    performance.MaxP95HostWorkMilliseconds,
                    performance.MaxP99HostWorkMilliseconds,
                    performance.MaxHostWorkMilliseconds,
                };
                if (durationBudgets.Any(value => value is { } number && (!double.IsFinite(number) || number <= 0)))
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' performance check '{performance.Id}' " +
                        "host-work budgets must be finite and greater than zero.");
                }
                if (performance.MaxManagedHeapGrowthBytes is < 0 || performance.MaxAllocatedBytes is < 0)
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' performance check '{performance.Id}' " +
                        "memory budgets cannot be negative.");
                }
                if (durationBudgets.All(value => value is null) &&
                    performance.MaxManagedHeapGrowthBytes is null &&
                    performance.MaxAllocatedBytes is null)
                {
                    throw new InvalidDataException(
                        $"Product smoke '{smoke.Id}' performance check '{performance.Id}' " +
                        "must declare at least one host-work or memory budget.");
                }
            }
        }

        if (manifest.WindowsRelease is null)
        {
            throw new InvalidDataException("Game manifest windowsRelease must be an object when declared.");
        }
        if (manifest.WindowsRelease.MaxPackageBytes is <= 0)
        {
            throw new InvalidDataException(
                "Game manifest windowsRelease.maxPackageBytes must be greater than zero when declared.");
        }
        if (manifest.WindowsRelease.MaxFileCount is <= 0)
        {
            throw new InvalidDataException(
                "Game manifest windowsRelease.maxFileCount must be greater than zero when declared.");
        }

        if (manifest.StaticCheckPaths is null)
        {
            throw new InvalidDataException("Game manifest staticCheckPaths must be an array when declared.");
        }
        foreach (var staticCheck in manifest.StaticCheckPaths)
        {
            if (staticCheck is null)
            {
                throw new InvalidDataException("Game manifest staticCheckPaths cannot contain null entries.");
            }
            ValidateCheckPatterns([staticCheck.Pattern], "Game manifest staticCheckPaths");
            if (string.IsNullOrWhiteSpace(staticCheck.Reason) ||
                staticCheck.Reason.Length is < 12 or > 200 ||
                staticCheck.Reason.Contains('\r', StringComparison.Ordinal) ||
                staticCheck.Reason.Contains('\n', StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Game manifest staticCheckPaths entry '{staticCheck.Pattern}' must declare a 12-200 character single-line reason.");
            }
        }

        var duplicateVisual = manifest.VisualTargets
            .GroupBy(target => target.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateVisual is not null)
        {
            throw new InvalidDataException($"Visual target ID '{duplicateVisual.Key}' is duplicated.");
        }
        foreach (var target in manifest.VisualTargets)
        {
            CodeNames.RequireSnakeCase(target.Id, nameof(target.Id));
            if (string.Equals(target.Id, "ui_components", StringComparison.Ordinal) ||
                string.Equals(target.Id, "rendered_probe", StringComparison.Ordinal) ||
                string.Equals(target.Id, "product", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Visual target ID '{target.Id}' is reserved.");
            }
            if (!target.ScenePath.StartsWith("res://", StringComparison.Ordinal) ||
                !target.ScenePath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Visual target '{target.Id}' has invalid scene path.");
            }
            if (!File.Exists(ToolFiles.ToAbsolutePath(root, target.ScenePath)))
            {
                throw new FileNotFoundException(
                    $"Visual target '{target.Id}' scene is missing.",
                    ToolFiles.ToAbsolutePath(root, target.ScenePath));
            }
            if (Path.IsPathRooted(target.BaselinePath) ||
                target.BaselinePath.Contains("..", StringComparison.Ordinal) ||
                !target.BaselinePath.Replace('\\', '/').StartsWith("tests/Visual/Baselines/", StringComparison.Ordinal) ||
                !target.BaselinePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Visual target '{target.Id}' baselinePath must be a PNG under tests/Visual/Baselines/.");
            }
            if (target.Width is < 64 or > 4096 || target.Height is < 64 or > 4096)
            {
                throw new InvalidDataException(
                    $"Visual target '{target.Id}' dimensions must be between 64 and 4096 pixels.");
            }
            if (target.CaptureMode is not ("SemanticControl" or "RenderedViewport"))
            {
                throw new InvalidDataException(
                    $"Visual target '{target.Id}' captureMode must be SemanticControl or RenderedViewport.");
            }
            if (target.CheckPaths is null)
            {
                throw new InvalidDataException(
                    $"Visual target '{target.Id}' checkPaths must be an array when declared.");
            }
            ValidateCheckPatterns(target.CheckPaths, $"Visual target '{target.Id}' checkPaths");
            if (target.ReadyFrames is < 1 or > 300)
            {
                throw new InvalidDataException(
                    $"Visual target '{target.Id}' readyFrames must be between 1 and 300.");
            }
            if (target.PixelTolerance is < 0 or > 1 || target.MaxChangedPixelRatio is < 0 or > 1)
            {
                throw new InvalidDataException(
                    $"Visual target '{target.Id}' pixel tolerances must be between 0 and 1.");
            }
            if (target.CaptureMode == "SemanticControl" &&
                (target.PixelTolerance != 0 || target.MaxChangedPixelRatio != 0))
            {
                throw new InvalidDataException(
                    $"Semantic visual target '{target.Id}' must use exact zero pixel tolerances.");
            }
            if (target.Pointer is { } pointer &&
                (pointer.X < 0 || pointer.X > target.Width || pointer.Y < 0 || pointer.Y > target.Height))
            {
                throw new InvalidDataException(
                    $"Visual target '{target.Id}' pointer must stay inside its capture dimensions.");
            }
        }

        var duplicate = manifest.Worlds.GroupBy(world => world.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"World ID '{duplicate.Key}' is duplicated.");
        }

        foreach (var world in manifest.Worlds)
        {
            CodeNames.RequireSnakeCase(world.Id, nameof(world.Id));
            CodeNames.RequireIdentifier(world.ClassName, nameof(world.ClassName));
            if (!NamespaceRegex().IsMatch(world.Namespace))
            {
                throw new InvalidDataException($"World '{world.Id}' has invalid namespace '{world.Namespace}'.");
            }
            if (!ProductLayout.IsProductNamespace(manifest, world.Namespace))
            {
                throw new InvalidDataException(
                    $"World '{world.Id}' must use the product namespace '{manifest.RootNamespace}'.");
            }
            if (!world.ScenePath.StartsWith("res://", StringComparison.Ordinal) ||
                !world.ScenePath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"World '{world.Id}' has invalid scene path '{world.ScenePath}'.");
            }

            var scenePath = ToolFiles.ToAbsolutePath(root, world.ScenePath);
            if (!File.Exists(scenePath))
            {
                throw new FileNotFoundException($"World '{world.Id}' scene is missing.", scenePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.InitialWorldId) &&
            manifest.Worlds.All(world => !string.Equals(world.Id, manifest.InitialWorldId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Initial world '{manifest.InitialWorldId}' is not registered in the game manifest.");
        }
    }

    private static string BuildGameCatalog(GameManifest manifest) =>
        $$"""
        // <auto-generated by LXFramework.Tools />
        #nullable enable

        namespace LX.Generated;

        public static class GameCatalog
        {
            public const string Name = "{{Escape(manifest.Name)}}";
            public const string RootNamespace = "{{Escape(manifest.RootNamespace)}}";
            public const string SourceRoot = "{{Escape(ProductLayout.GetSourceRoot(manifest))}}";
            public const string InitialWorldId = "{{Escape(manifest.InitialWorldId)}}";
            public const bool HasProduct = {{(!string.IsNullOrWhiteSpace(manifest.Name) ? "true" : "false")}};
        }
        """ + "\n";

    private static string BuildWorldCatalog(GameManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated by LXFramework.Tools />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("using LX.Scenes;");
        builder.AppendLine();
        builder.AppendLine("namespace LX.Generated;");
        builder.AppendLine();
        builder.AppendLine("public static class WorldCatalog");
        builder.AppendLine("{");
        foreach (var world in manifest.Worlds.OrderBy(world => world.Id, StringComparer.Ordinal))
        {
            builder.AppendLine($"    public static readonly WorldDescriptor {CodeNames.ToPascalCase(world.Id)} = new(");
            builder.AppendLine($"        new WorldId(\"{Escape(world.Id)}\"),");
            builder.AppendLine($"        \"{Escape(world.ScenePath)}\");");
            builder.AppendLine();
        }

        builder.Append("    public static IReadOnlyList<WorldDescriptor> All { get; } = [");
        builder.Append(string.Join(", ", manifest.Worlds
            .OrderBy(world => world.Id, StringComparer.Ordinal)
            .Select(world => CodeNames.ToPascalCase(world.Id))));
        builder.AppendLine("];\n}");
        return builder.ToString();
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool IsSingleLineMarker(string marker) =>
        !string.IsNullOrWhiteSpace(marker) &&
        !marker.Contains('\r', StringComparison.Ordinal) &&
        !marker.Contains('\n', StringComparison.Ordinal);

    private static void ValidateCheckPatterns(IEnumerable<string> patterns, string owner)
    {
        foreach (var pattern in patterns)
        {
            if (!ProductSmokeImpact.IsValidPattern(pattern))
            {
                throw new InvalidDataException(
                    $"{owner} entry '{pattern}' must be a normalized Godot-root/workspace-relative glob " +
                    "without '.', '..', backslashes, or drive prefixes.");
            }
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceRegex();
}
