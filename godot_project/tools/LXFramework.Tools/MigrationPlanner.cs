using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace LXFramework.Tools;

internal static class MigrationPlanner
{
    private const string Schema = "lx.game-migration-plan";
    private const int SchemaVersion = 1;
    private const int MaxInventoryFiles = 100_000;
    private const int MaxSamplesPerCategory = 40;

    public static int Run(string root, IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 1 && arguments[0] is "--help" or "-h" or "help")
        {
            return Usage(0);
        }
        if (arguments.Count == 0 || !string.Equals(arguments[0], "plan", StringComparison.OrdinalIgnoreCase))
        {
            return Usage(2);
        }

        string? source = null;
        var mode = MigrationMode.Port;
        var requestedEngine = SourceEngine.Auto;
        for (var index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--source" when index + 1 < arguments.Count:
                    source = arguments[++index];
                    break;
                case "--mode" when index + 1 < arguments.Count:
                    if (!Enum.TryParse<MigrationMode>(arguments[++index], ignoreCase: true, out mode))
                    {
                        Console.Error.WriteLine("migrate: mode must be upgrade, port, or remake.");
                        return 2;
                    }
                    break;
                case "--engine" when index + 1 < arguments.Count:
                    if (!Enum.TryParse<SourceEngine>(arguments[++index], ignoreCase: true, out requestedEngine))
                    {
                        Console.Error.WriteLine(
                            "migrate: engine must be auto, lxframework, godot, unity, unreal, cocos, or custom.");
                        return 2;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"migrate: unknown or incomplete argument '{arguments[index]}'.");
                    return 2;
            }
        }
        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine("migrate: --source is required.");
            return 2;
        }

        var inventory = ReadInventory(root, source);
        var detectedEngine = requestedEngine == SourceEngine.Auto
            ? DetectEngine(inventory.Paths)
            : requestedEngine;
        if (mode == MigrationMode.Upgrade &&
            detectedEngine is not (SourceEngine.LXFramework or SourceEngine.Godot))
        {
            Console.Error.WriteLine(
                $"migrate: upgrade mode requires an LXFramework or Godot source; detected {detectedEngine}.");
            return 2;
        }

        var categories = inventory.Paths
            .GroupBy(Classify)
            .OrderBy(group => group.Key)
            .Select(group => new MigrationCategorySummary(
                group.Key,
                group.Count(),
                group.Take(MaxSamplesPerCategory).ToArray(),
                group.Count() > MaxSamplesPerCategory))
            .ToArray();
        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        var plan = new GameMigrationPlan(
            Schema,
            SchemaVersion,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            mode,
            new MigrationSource(
                inventory.Kind,
                source,
                inventory.ResolvedSource,
                detectedEngine,
                inventory.Fingerprint,
                inventory.Paths.Count),
            new MigrationTarget(
                Directory.GetParent(root)?.FullName ?? root,
                game.Name,
                game.RootNamespace,
                game.SourceRoot),
            categories,
            BuildDecisions(mode, detectedEngine),
            BuildGates(mode));

        var output = Path.Combine(root, ".lx", "migration", plan.Id + ".json");
        ToolFiles.WriteJson(output, plan);
        Console.WriteLine(
            $"migration plan {plan.Id}: mode={mode}, engine={detectedEngine}, " +
            $"files={inventory.Paths.Count} -> {ToolFiles.Relative(root, output)}");
        foreach (var category in categories)
        {
            Console.WriteLine($"{category.Category,-20} {category.Count,6}");
        }
        return 0;
    }

    internal static string? ValidateClassifier()
    {
        var cases = new Dictionary<string, MigrationFileCategory>(StringComparer.Ordinal)
        {
            ["godot_project/script/OldGame/GameRoot.cs"] = MigrationFileCategory.ProductSource,
            ["godot_project/script/OldGame/Generated/Catalog.g.cs"] = MigrationFileCategory.Generated,
            ["godot_project/src/LXFramework/Runtime/LXHost.cs"] = MigrationFileCategory.FrameworkOwned,
            ["godot_project/build/windows/Game.exe"] = MigrationFileCategory.BuildArtifact,
            ["game_design/data/levels.json"] = MigrationFileCategory.ProductFact,
            ["Assets/Scenes/Main.unity"] = MigrationFileCategory.Scene,
            ["Content/Audio/theme.ogg"] = MigrationFileCategory.Asset,
        };
        foreach (var (path, expected) in cases)
        {
            var actual = Classify(path);
            if (actual != expected)
            {
                return $"Migration classifier mapped '{path}' to {actual}, expected {expected}.";
            }
        }
        if (DetectEngine(["Assets/Test.cs", "ProjectSettings/ProjectVersion.txt"]) != SourceEngine.Unity ||
            DetectEngine(["godot_project/project.godot", "godot_project/content/game/game-manifest.json"]) !=
            SourceEngine.LXFramework)
        {
            return "Migration engine detection did not recognize representative Unity/LXFramework layouts.";
        }
        return null;
    }

    private static MigrationInventory ReadInventory(string root, string source)
    {
        var workspaceRoot = Directory.GetParent(root)?.FullName ?? root;
        var localCandidate = Path.GetFullPath(
            Path.IsPathRooted(source) ? source : Path.Combine(workspaceRoot, source));
        if (Directory.Exists(localCandidate))
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            var paths = Directory.EnumerateFiles(localCandidate, "*", options)
                .Select(path => Path.GetRelativePath(localCandidate, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(MaxInventoryFiles + 1)
                .ToArray();
            if (paths.Length > MaxInventoryFiles)
            {
                throw new InvalidDataException(
                    $"Migration source exceeds the bounded inventory limit of {MaxInventoryFiles} files.");
            }
            return new MigrationInventory(
                MigrationSourceKind.Directory,
                localCandidate,
                paths,
                ComputeInventoryHash(paths));
        }

        var commit = RunGit(workspaceRoot, ["rev-parse", "--verify", "--end-of-options", source + "^{commit}"])
            .Trim();
        if (commit.Length != 40 || !commit.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException($"Migration source '{source}' is neither a directory nor a Git commit.");
        }
        var files = RunGit(workspaceRoot, ["ls-tree", "-r", "--name-only", commit, "--"])
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(MaxInventoryFiles + 1)
            .ToArray();
        if (files.Length > MaxInventoryFiles)
        {
            throw new InvalidDataException(
                $"Migration source exceeds the bounded inventory limit of {MaxInventoryFiles} files.");
        }
        return new MigrationInventory(MigrationSourceKind.GitCommit, commit, files, commit);
    }

    private static string RunGit(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(error.Trim());
        }
        return output;
    }

    private static SourceEngine DetectEngine(IReadOnlyCollection<string> paths)
    {
        var normalized = paths.Select(path => path.Replace('\\', '/')).ToArray();
        if (normalized.Any(path => path.EndsWith("content/game/game-manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            return SourceEngine.LXFramework;
        }
        if (normalized.Any(path => path.EndsWith("ProjectSettings/ProjectVersion.txt", StringComparison.OrdinalIgnoreCase)))
        {
            return SourceEngine.Unity;
        }
        if (normalized.Any(path => path.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)))
        {
            return SourceEngine.Unreal;
        }
        if (normalized.Any(path => Path.GetFileName(path).Equals("project.godot", StringComparison.OrdinalIgnoreCase)))
        {
            return SourceEngine.Godot;
        }
        if (normalized.Any(path => path.EndsWith("settings/project.json", StringComparison.OrdinalIgnoreCase)) &&
            normalized.Any(path => path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)))
        {
            return SourceEngine.Cocos;
        }
        return SourceEngine.Custom;
    }

    private static MigrationFileCategory Classify(string input)
    {
        var path = input.Replace('\\', '/').TrimStart('/');
        var lower = path.ToLowerInvariant();
        var segments = lower.Split('/');
        var extension = Path.GetExtension(lower);
        if (segments.Any(segment => segment is ".git" or ".godot" or ".lx" or "bin" or "obj" or
                "build" or "dist" or "artifacts" or "library" or "temp") ||
            extension is ".exe" or ".dll" or ".pdb" or ".so" or ".dylib" or ".apk" or ".aab")
        {
            return MigrationFileCategory.BuildArtifact;
        }
        if (segments.Contains("generated") ||
            lower.Contains("content/data/luban/", StringComparison.Ordinal) ||
            lower.EndsWith(".g.cs", StringComparison.Ordinal))
        {
            return MigrationFileCategory.Generated;
        }
        if (lower is "agents.md" or "readme.md" or "lx.ps1" ||
            lower.StartsWith(".agents/", StringComparison.Ordinal) ||
            lower.StartsWith(".codex/", StringComparison.Ordinal) ||
            lower.StartsWith("books/", StringComparison.Ordinal) ||
            lower.StartsWith("godot_project/src/lxframework", StringComparison.Ordinal) ||
            lower.StartsWith("godot_project/tools/", StringComparison.Ordinal) ||
            lower.StartsWith("godot_project/addons/lx_tools/", StringComparison.Ordinal) ||
            lower == "godot_project/lx.ps1")
        {
            return MigrationFileCategory.FrameworkOwned;
        }
        if (lower.StartsWith("game_design/", StringComparison.Ordinal) ||
            lower.Contains("/content/game/", StringComparison.Ordinal) ||
            lower.Contains("/content/features/", StringComparison.Ordinal) ||
            lower.Contains("/content/input/", StringComparison.Ordinal) ||
            lower.Contains("/content/res/", StringComparison.Ordinal) ||
            lower.Contains("/content/ui/", StringComparison.Ordinal))
        {
            return MigrationFileCategory.ProductFact;
        }
        if (lower.EndsWith("project.godot", StringComparison.Ordinal) ||
            lower.EndsWith("export_presets.cfg", StringComparison.Ordinal) ||
            lower.EndsWith("projectsettings/projectversion.txt", StringComparison.Ordinal) ||
            lower.EndsWith(".uproject", StringComparison.Ordinal))
        {
            return MigrationFileCategory.Integration;
        }
        if (extension is ".tscn" or ".scn" or ".tres" or ".unity" or ".prefab" or ".umap" or ".uasset")
        {
            return MigrationFileCategory.Scene;
        }
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg" or ".ogg" or ".mp3" or
            ".wav" or ".flac" or ".ttf" or ".otf" or ".glb" or ".gltf" or ".fbx" or ".blend")
        {
            return MigrationFileCategory.Asset;
        }
        if (extension is ".cs" or ".gd" or ".cpp" or ".c" or ".h" or ".hpp" or ".lua" or ".ts" or
            ".js" or ".java" or ".kt" or ".swift")
        {
            return MigrationFileCategory.ProductSource;
        }
        if (extension is ".json" or ".xml" or ".csv" or ".yaml" or ".yml" or ".bytes" or ".toml")
        {
            return MigrationFileCategory.Data;
        }
        if (lower.Contains("license", StringComparison.Ordinal) || lower.Contains("copyright", StringComparison.Ordinal) ||
            extension is ".md" or ".txt")
        {
            return MigrationFileCategory.DocumentationOrLicense;
        }
        return MigrationFileCategory.ManualReview;
    }

    private static IReadOnlyList<string> BuildDecisions(MigrationMode mode, SourceEngine engine) => mode switch
    {
        MigrationMode.Upgrade =>
        [
            "Keep the target checkout's framework, tools, Codex workflow, and root documentation.",
            "Migrate product facts, product source, scenes, and authorized assets; regenerate derived outputs.",
            "Compile against the target Godot/LX API and treat handwritten API drift as manual work.",
        ],
        MigrationMode.Port =>
        [
            $"Preserve source semantics from {engine}; do not copy its service locator, event bus, lifecycle, resource, scene, or UI managers into LXFramework.",
            "Map gameplay, input, UI, data, save, audio, scene, and asset ownership to typed LX capabilities.",
            "Create one playable vertical slice before migrating the remaining content.",
        ],
        _ =>
        [
            $"Treat the {engine} source as behavioral reference; do not mechanically translate engine-specific code.",
            "Reuse only code and assets whose authorization and license are known.",
            "Specify parity scenarios for state transitions, controls, timing, UI, data, save behavior, and restart closure before implementation.",
        ],
    };

    private static IReadOnlyList<string> BuildGates(MigrationMode mode)
    {
        var gates = new List<string>
        {
            "source-inventory-reviewed",
            "framework-owned-files-unchanged",
            "generated-and-build-artifacts-not-copied",
            "lx-capability-map-complete",
            "vertical-slice-check-pass",
            "product-smoke-pass",
            "runtime-state-observed",
            "full-validation-pass",
        };
        if (mode != MigrationMode.Upgrade)
        {
            gates.Insert(1, "source-license-and-asset-authorization-reviewed");
            gates.Add("behavior-parity-evidence-reviewed");
        }
        return gates;
    }

    private static string ComputeInventoryHash(IEnumerable<string> paths)
    {
        var content = string.Join('\n', paths);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    private static int Usage(int exitCode)
    {
        Console.Error.WriteLine(
            "Usage: lx migrate plan --source <directory|git-ref> " +
            "[--mode upgrade|port|remake] [--engine auto|lxframework|godot|unity|unreal|cocos|custom]");
        return exitCode;
    }

    private sealed record MigrationInventory(
        MigrationSourceKind Kind,
        string ResolvedSource,
        IReadOnlyList<string> Paths,
        string Fingerprint);
}

internal enum MigrationMode
{
    Upgrade,
    Port,
    Remake,
}

internal enum SourceEngine
{
    Auto,
    LXFramework,
    Godot,
    Unity,
    Unreal,
    Cocos,
    Custom,
}

internal enum MigrationSourceKind
{
    Directory,
    GitCommit,
}

internal enum MigrationFileCategory
{
    Asset,
    BuildArtifact,
    Data,
    DocumentationOrLicense,
    FrameworkOwned,
    Generated,
    Integration,
    ManualReview,
    ProductFact,
    ProductSource,
    Scene,
}

internal sealed record GameMigrationPlan(
    string Schema,
    int SchemaVersion,
    string Id,
    DateTimeOffset CreatedAtUtc,
    MigrationMode Mode,
    MigrationSource Source,
    MigrationTarget Target,
    IReadOnlyList<MigrationCategorySummary> Categories,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<string> RequiredGates);

internal sealed record MigrationSource(
    MigrationSourceKind Kind,
    string Requested,
    string Resolved,
    SourceEngine Engine,
    string Fingerprint,
    int FileCount);

internal sealed record MigrationTarget(
    string WorkspaceRoot,
    string ProductName,
    string RootNamespace,
    string SourceRoot);

internal sealed record MigrationCategorySummary(
    MigrationFileCategory Category,
    int Count,
    IReadOnlyList<string> SamplePaths,
    bool Truncated);
