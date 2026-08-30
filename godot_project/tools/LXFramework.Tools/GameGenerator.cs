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
            if (string.Equals(smoke.Id, "framework", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Product smoke ID 'framework' is reserved for the built-in framework smoke.");
            }
            if (!smoke.Argument.StartsWith("--", StringComparison.Ordinal) ||
                smoke.Argument.Any(char.IsWhiteSpace))
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' must declare one '--' prefixed user argument.");
            }
            if (string.IsNullOrWhiteSpace(smoke.SuccessMarker) ||
                smoke.SuccessMarker.Contains('\r', StringComparison.Ordinal) ||
                smoke.SuccessMarker.Contains('\n', StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' must declare a single-line success marker.");
            }
            if (smoke.TimeoutSeconds is < 1 or > 300)
            {
                throw new InvalidDataException(
                    $"Product smoke '{smoke.Id}' timeoutSeconds must be between 1 and 300.");
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

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceRegex();
}
