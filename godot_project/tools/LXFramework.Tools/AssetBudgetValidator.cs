namespace LXFramework.Tools;

internal static class AssetBudgetValidator
{
    public static IReadOnlyList<string> ValidateSource(string root, AssetManifest manifest)
    {
        var errors = new List<string>();
        if (manifest.Budgets is null)
        {
            errors.Add("Resource manifest budgets must be an object when declared.");
            return errors;
        }

        ValidatePositive(manifest.Budgets.MaxAssetCount, "budgets.maxAssetCount", errors);
        ValidatePositive(manifest.Budgets.MaxSourceBytes, "budgets.maxSourceBytes", errors);
        ValidatePositive(manifest.Budgets.MaxSingleSourceBytes, "budgets.maxSingleSourceBytes", errors);
        ValidatePositive(manifest.Budgets.MaxImportArtifactBytes, "budgets.maxImportArtifactBytes", errors);

        foreach (var duplicate in manifest.Assets
                     .GroupBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group
                         .Select(asset => asset.Path)
                         .Distinct(StringComparer.Ordinal)
                         .Count() > 1))
        {
            errors.Add($"Resource path '{duplicate.Key}' is registered with conflicting path casing.");
        }

        var sourceBytes = 0L;
        var countedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            ValidatePositive(asset.MaxSourceBytes, $"asset '{asset.Id}' maxSourceBytes", errors);
            var assetPath = ToolFiles.ToAbsolutePath(root, asset.Path);
            if (!File.Exists(assetPath))
            {
                errors.Add($"Resource '{asset.Path}' is missing.");
                continue;
            }

            if (!HasExactPathCasing(root, asset.Path))
            {
                errors.Add($"Resource path '{asset.Path}' does not match the on-disk path casing.");
            }

            var length = new FileInfo(assetPath).Length;
            if (countedSourcePaths.Add(assetPath))
            {
                sourceBytes += length;
            }
            if (asset.MaxSourceBytes is { } assetLimit && length > assetLimit)
            {
                errors.Add(
                    $"Resource '{asset.Path}' is {length} bytes and exceeds its maxSourceBytes budget of {assetLimit} bytes.");
            }
            if (manifest.Budgets.MaxSingleSourceBytes is { } singleLimit && length > singleLimit)
            {
                errors.Add(
                    $"Resource '{asset.Path}' is {length} bytes and exceeds budgets.maxSingleSourceBytes of {singleLimit} bytes.");
            }
        }

        if (manifest.Budgets.MaxAssetCount is { } countLimit && manifest.Assets.Count > countLimit)
        {
            errors.Add(
                $"Resource manifest contains {manifest.Assets.Count} assets and exceeds budgets.maxAssetCount of {countLimit}.");
        }
        if (manifest.Budgets.MaxSourceBytes is { } sourceLimit && sourceBytes > sourceLimit)
        {
            errors.Add(
                $"Registered resource sources total {sourceBytes} bytes and exceed budgets.maxSourceBytes of {sourceLimit} bytes.");
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateImported(string root)
    {
        var manifestPath = Path.Combine(root, "content", "res", "res-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        var manifest = ToolFiles.ReadJson<AssetManifest>(manifestPath);
        var limit = manifest.Budgets?.MaxImportArtifactBytes;
        if (limit is null)
        {
            return [];
        }

        var importedRoot = Path.Combine(root, ".godot", "imported");
        if (!Directory.Exists(importedRoot))
        {
            return ["Resource import budget is declared, but Godot produced no .godot/imported directory."];
        }

        var importedBytes = Directory.EnumerateFiles(importedRoot, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        return importedBytes <= limit.Value
            ? []
            :
            [
                $"Godot import artifacts total {importedBytes} bytes and exceed " +
                $"budgets.maxImportArtifactBytes of {limit.Value} bytes.",
            ];
    }

    public static AssetBudgetReport Evaluate(string root, bool includeImported)
    {
        var manifest = ToolFiles.ReadJson<AssetManifest>(
            Path.Combine(root, "content", "res", "res-manifest.json"));
        var sourceFiles = manifest.Assets
            .Select(asset => ToolFiles.ToAbsolutePath(root, asset.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .Select(path => new FileInfo(path).Length)
            .ToArray();
        long? importArtifactBytes = null;
        if (includeImported)
        {
            var importedRoot = Path.Combine(root, ".godot", "imported");
            importArtifactBytes = Directory.Exists(importedRoot)
                ? Directory.EnumerateFiles(importedRoot, "*", SearchOption.AllDirectories)
                    .Sum(path => new FileInfo(path).Length)
                : 0;
        }

        var errors = ValidateSource(root, manifest).ToList();
        if (includeImported)
        {
            errors.AddRange(ValidateImported(root));
        }
        return new AssetBudgetReport(
            "lx.asset-budget-report",
            1,
            DateTimeOffset.UtcNow,
            errors.Count == 0,
            manifest.Assets.Count,
            sourceFiles.Sum(),
            sourceFiles.DefaultIfEmpty().Max(),
            importArtifactBytes,
            manifest.Budgets,
            errors);
    }

    public static string? ValidateProtocol(string root)
    {
        var manifest = ToolFiles.ReadJson<AssetManifest>(
            Path.Combine(root, "content", "res", "res-manifest.json"));
        var valid = new AssetManifest
        {
            Assets = manifest.Assets,
            Budgets = new AssetBudgetManifest
            {
                MaxAssetCount = Math.Max(1, manifest.Assets.Count),
                MaxSourceBytes = long.MaxValue,
                MaxSingleSourceBytes = long.MaxValue,
            },
        };
        if (ValidateSource(root, valid).Count != 0)
        {
            return "Asset budget protocol rejected the registered baseline under permissive limits.";
        }

        var oversized = new AssetManifest
        {
            Assets =
            [
                .. manifest.Assets,
                new AssetManifestEntry
                {
                    Id = "budget_fixture",
                    Path = "res://validation-budget-fixture.missing",
                },
            ],
            Budgets = new AssetBudgetManifest
            {
                MaxAssetCount = Math.Max(1, manifest.Assets.Count),
            },
        };
        if (!ValidateSource(root, oversized).Any(error =>
                error.Contains("maxAssetCount", StringComparison.Ordinal)))
        {
            return "Asset budget protocol accepted an asset count above the declared limit.";
        }
        return null;
    }

    private static void ValidatePositive(long? value, string name, ICollection<string> errors)
    {
        if (value is <= 0)
        {
            errors.Add($"Resource manifest {name} must be greater than zero when declared.");
        }
    }

    private static void ValidatePositive(int? value, string name, ICollection<string> errors)
    {
        if (value is <= 0)
        {
            errors.Add($"Resource manifest {name} must be greater than zero when declared.");
        }
    }

    private static bool HasExactPathCasing(string root, string resourcePath)
    {
        var current = Path.GetFullPath(root);
        foreach (var segment in resourcePath[6..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current))
            {
                return false;
            }

            var match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), segment, StringComparison.Ordinal));
            if (match is null)
            {
                return false;
            }
            current = match;
        }
        return true;
    }
}

internal sealed record AssetBudgetReport(
    string Schema,
    int SchemaVersion,
    DateTimeOffset EvaluatedAtUtc,
    bool Success,
    int AssetCount,
    long SourceBytes,
    long LargestSourceBytes,
    long? ImportArtifactBytes,
    AssetBudgetManifest Limits,
    IReadOnlyList<string> Errors);
