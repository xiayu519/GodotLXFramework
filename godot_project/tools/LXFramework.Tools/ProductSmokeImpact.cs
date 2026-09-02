using System.Text;
using System.Text.RegularExpressions;

namespace LXFramework.Tools;

internal static class ProductSmokeImpact
{
    public static IReadOnlyList<ProductSmokeManifestEntry> SelectAffected(
        IEnumerable<ProductSmokeManifestEntry> smokes,
        IEnumerable<string> changedPaths)
    {
        var normalizedPaths = changedPaths
            .Select(NormalizeChangedPath)
            .Where(path => path.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return [];
        }

        return smokes
            .Where(smoke => smoke.CheckPaths.Any(pattern =>
                normalizedPaths.Any(path => Matches(pattern, path))))
            .ToArray();
    }

    public static IReadOnlyList<VisualTargetManifestEntry> SelectAffectedVisuals(
        IEnumerable<VisualTargetManifestEntry> targets,
        IEnumerable<string> changedPaths)
    {
        var normalizedPaths = NormalizeChangedPaths(changedPaths);
        return targets
            .Where(target => target.CheckPaths.Any(pattern =>
                normalizedPaths.Any(path => Matches(pattern, path))))
            .ToArray();
    }

    public static ProductValidationImpact Analyze(
        GameManifest game,
        IEnumerable<string> changedPaths)
    {
        var normalizedPaths = NormalizeChangedPaths(changedPaths);
        var smokes = SelectAffected(game.GetProductSmokes(), normalizedPaths);
        var visuals = SelectAffectedVisuals(game.VisualTargets, normalizedPaths);
        var mappings = normalizedPaths.Select(path =>
        {
            var gates = new List<string>();
            gates.AddRange(smokes
                .Where(smoke => smoke.CheckPaths.Any(pattern => Matches(pattern, path)))
                .Select(smoke => $"smoke:{smoke.Id}"));
            gates.AddRange(visuals
                .Where(target => target.CheckPaths.Any(pattern => Matches(pattern, path)))
                .Select(target => $"visual:{target.Id}"));
            foreach (var staticCheck in game.StaticCheckPaths.Where(entry => Matches(entry.Pattern, path)))
            {
                gates.Add($"static-only:{staticCheck.Reason}");
            }
            if (!IsProductRuntimePath(path))
            {
                gates.Add("not-product-runtime");
            }
            if (string.IsNullOrWhiteSpace(game.Name))
            {
                gates.Add("no-product");
            }
            return new ProductValidationPathMapping(path, gates);
        }).ToArray();

        var unmatchedRuntimePaths = string.IsNullOrWhiteSpace(game.Name)
            ? []
            : mappings
                .Where(mapping => IsProductRuntimePath(mapping.Path) && mapping.Gates.Count == 0)
                .Select(mapping => mapping.Path)
                .ToArray();
        return new ProductValidationImpact(smokes, visuals, mappings, unmatchedRuntimePaths);
    }

    public static string NormalizeChangedPath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        normalized = normalized.TrimStart('/');
        const string projectPrefix = "godot_project/";
        if (normalized.StartsWith(projectPrefix, StringComparison.Ordinal))
        {
            normalized = normalized[projectPrefix.Length..];
        }
        return normalized;
    }

    private static string[] NormalizeChangedPaths(IEnumerable<string> changedPaths) =>
        changedPaths
            .Select(NormalizeChangedPath)
            .Where(path => path.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static bool IsValidPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) ||
            pattern.Contains('\\') ||
            pattern.StartsWith("/", StringComparison.Ordinal) ||
            pattern.StartsWith("./", StringComparison.Ordinal) ||
            pattern.StartsWith("godot_project/", StringComparison.Ordinal) ||
            pattern.Contains("//", StringComparison.Ordinal) ||
            pattern.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return false;
        }

        return !Path.IsPathRooted(pattern) && pattern.IndexOf(':') < 0;
    }

    public static bool IsValidChangedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        var normalized = NormalizeChangedPath(path);
        return normalized.Length != 0 &&
               normalized.IndexOf(':') < 0 &&
               !normalized.Split('/').Any(segment => segment is "" or "." or "..");
    }

    public static string? ValidateClassifier()
    {
        var game = new GameManifest
        {
            Name = "ImpactFixture",
            ProductSmokes =
            [
                new ProductSmokeManifestEntry
                {
                    Id = "story",
                    CheckPaths = ["script/Fixture/EventRuntime/**"],
                },
            ],
            VisualTargets =
            [
                new VisualTargetManifestEntry
                {
                    Id = "hud",
                    CheckPaths = ["scene/ui/hud/**"],
                },
            ],
            StaticCheckPaths =
            [
                new StaticCheckPathManifestEntry
                {
                    Pattern = "content/schema/**",
                    Reason = "schema references are exhaustively validated",
                },
            ],
        };
        var covered = Analyze(game,
        [
            "godot_project/script/Fixture/EventRuntime/Runner.cs",
            "scene/ui/hud/Hud.tscn",
            "content/schema/story.json",
        ]);
        if (covered.UnmatchedRuntimePaths.Count != 0 ||
            covered.Smokes.Count != 1 ||
            covered.Visuals.Count != 1 ||
            covered.Mappings.Any(mapping => mapping.Gates.Count == 0))
        {
            return "Validation impact classifier did not map smoke, visual, and static-only fixtures.";
        }

        var uncovered = Analyze(game, ["content/story/chapter_02.json"]);
        if (uncovered.UnmatchedRuntimePaths.Count != 1)
        {
            return "Validation impact classifier accepted an uncovered product runtime path.";
        }
        var framework = Analyze(game,
        [
            "src/LXFramework/Media/VideoSequencePlayer.cs",
            "lx.ps1",
        ]);
        if (framework.UnmatchedRuntimePaths.Count != 0 ||
            framework.Mappings.Any(mapping =>
                mapping.Gates.All(gate => gate != "not-product-runtime")))
        {
            return "Validation impact classifier treated framework sources as unmapped product runtime content.";
        }
        return null;
    }

    internal static bool Matches(string pattern, string changedPath)
    {
        var expression = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];
            if (current == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                if (index + 2 < pattern.Length && pattern[index + 2] == '/')
                {
                    expression.Append("(?:.*/)?");
                    index += 2;
                }
                else
                {
                    expression.Append(".*");
                    index++;
                }
                continue;
            }
            if (current == '*')
            {
                expression.Append("[^/]*");
                continue;
            }
            if (current == '?')
            {
                expression.Append("[^/]");
                continue;
            }
            expression.Append(Regex.Escape(current.ToString()));
        }
        expression.Append('$');
        return Regex.IsMatch(
            changedPath,
            expression.ToString(),
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }


    private static bool IsProductRuntimePath(string path)
    {
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/Generated/", StringComparison.Ordinal) ||
            path.StartsWith(".agents/", StringComparison.Ordinal) ||
            path.StartsWith(".codex/", StringComparison.Ordinal) ||
            path.StartsWith(".github/", StringComparison.Ordinal) ||
            path.StartsWith("Books/", StringComparison.Ordinal) ||
            path.StartsWith("tools/", StringComparison.Ordinal) ||
            path.StartsWith("tests/", StringComparison.Ordinal) ||
            path.StartsWith("src/LXFramework", StringComparison.Ordinal) ||
            path.StartsWith("addons/lx_tools/", StringComparison.Ordinal) ||
            path.StartsWith("api/", StringComparison.Ordinal) ||
            path.StartsWith(".lx/", StringComparison.Ordinal) ||
            path.StartsWith(".godot/", StringComparison.Ordinal) ||
            path.Contains("/bin/", StringComparison.Ordinal) ||
            path.Contains("/obj/", StringComparison.Ordinal) ||
            path is "AGENTS.md" or "README.md" or "Directory.Build.props" or
                "LXFramework.csproj" or "LXFramework.sln" or "project.godot" or
                "export_presets.cfg" or "lx.ps1" or "scene/main.tscn")
        {
            return false;
        }

        return true;
    }
}

internal sealed record ProductValidationImpact(
    IReadOnlyList<ProductSmokeManifestEntry> Smokes,
    IReadOnlyList<VisualTargetManifestEntry> Visuals,
    IReadOnlyList<ProductValidationPathMapping> Mappings,
    IReadOnlyList<string> UnmatchedRuntimePaths);

internal sealed record ProductValidationPathMapping(
    string Path,
    IReadOnlyList<string> Gates);
