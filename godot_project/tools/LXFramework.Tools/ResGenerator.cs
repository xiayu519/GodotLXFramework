using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace LXFramework.Tools;

internal static partial class ResGenerator
{
    private const string ManifestRelativePath = "content/res/res-manifest.json";
    private const int EntriesPerFile = 256;

    public static IReadOnlyDictionary<string, string> BuildOutputs(string root)
    {
        var manifestPath = Path.Combine(root, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Resource manifest is missing.", manifestPath);
        }

        var manifest = ToolFiles.ReadJson<AssetManifest>(manifestPath);
        if (manifest.Version != 1)
        {
            throw new InvalidDataException($"Unsupported resource manifest version {manifest.Version}.");
        }

        var duplicate = manifest.Assets
            .GroupBy(asset => asset.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Resource ID '{duplicate.Key}' is duplicated.");
        }

        var generatedNameCollision = manifest.Assets
            .GroupBy(asset => CodeNames.ToPascalCase(asset.Id), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (generatedNameCollision is not null)
        {
            throw new InvalidDataException(
                $"Resource IDs collide as generated name '{generatedNameCollision.Key}'.");
        }

        foreach (var asset in manifest.Assets)
        {
            Validate(root, asset);
        }
        ValidatePartitions(manifest);
        ValidateGroups(manifest);

        var game = ToolFiles.ReadJson<GameManifest>(
            Path.Combine(root, "content", "game", "game-manifest.json"));
        if (manifest.Assets.Any(asset => asset.Scope == ManifestScopes.Product) &&
            string.IsNullOrWhiteSpace(game.Name))
        {
            throw new InvalidDataException(
                "Product resources require a declared game and product sourceRoot.");
        }

        var outputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in new[] { ManifestScopes.Framework, ManifestScopes.Product })
        {
            var scopedAssets = manifest.Assets
                .Where(asset => asset.Scope == scope)
                .OrderBy(asset => asset.Id, StringComparer.Ordinal)
                .ToArray();
            if (scopedAssets.Length == 0)
            {
                continue;
            }

            var outputRoot = scope == ManifestScopes.Framework
                ? Path.Combine(root, "src", "LXFramework", "Generated", "Res")
                : Path.Combine(ProductLayout.GetGeneratedDirectory(root, game), "Res");
            AddScopeOutputs(outputs, outputRoot, scope, scopedAssets);
        }

        return outputs;
    }

    public static string? ValidatePartitioning()
    {
        var assets = Enumerable.Range(0, 600)
            .Select(index => new AssetManifestEntry
            {
                Scope = ManifestScopes.Framework,
                Id = $"fixture_{index:000}",
                ResourceType = "Texture2D",
                Path = $"res://fixture/{index:000}.png",
                CachePolicy = "Cached",
                Group = "fixture_group",
            })
            .ToArray();
        var outputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddScopeOutputs(outputs, "fixture", ManifestScopes.Framework, assets);
        var fieldParts = outputs.Keys.Count(path =>
            Path.GetFileName(path).StartsWith("ResCatalog.Framework.FixtureGroup.B", StringComparison.Ordinal));
        var groupParts = outputs.Keys.Count(path =>
            Path.GetFileName(path).StartsWith(
                "ResCatalog.Framework.Group.FixtureGroup.B",
                StringComparison.Ordinal));
        if (fieldParts == 0 || fieldParts != groupParts || outputs.Count != fieldParts + groupParts + 1 ||
            outputs.Where(output => Path.GetFileName(output.Key).StartsWith(
                    "ResCatalog.Framework.FixtureGroup.B",
                    StringComparison.Ordinal))
                .Any(output => CountOccurrences(output.Value, "public static readonly AssetRef<") > EntriesPerFile) ||
            outputs.Where(output => Path.GetFileName(output.Key).StartsWith(
                    "ResCatalog.Framework.Group.FixtureGroup.B",
                    StringComparison.Ordinal))
                .Any(output => CountOccurrences(output.Value, "(\"fixture_") > EntriesPerFile))
        {
            return "Resource catalog partitioning did not split 600 fields and group entries into bounded partial files.";
        }

        var expandedAssets = assets.Append(new AssetManifestEntry
        {
            Scope = ManifestScopes.Framework,
            Id = "fixture_new",
            ResourceType = "Texture2D",
            Path = "res://fixture/new.png",
            CachePolicy = "Cached",
            Group = "fixture_group",
        }).ToArray();
        var expandedOutputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddScopeOutputs(expandedOutputs, "fixture", ManifestScopes.Framework, expandedAssets);
        var changedOutputs = outputs.Keys
            .Union(expandedOutputs.Keys, StringComparer.Ordinal)
            .Count(path => !outputs.TryGetValue(path, out var before) ||
                           !expandedOutputs.TryGetValue(path, out var after) ||
                           !string.Equals(before, after, StringComparison.Ordinal));
        if (changedOutputs != 2)
        {
            return "Adding one resource rewrote files outside its stable field and group buckets.";
        }
        return null;
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(pattern, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += pattern.Length;
        }
        return count;
    }

    private static void AddScopeOutputs(
        IDictionary<string, string> outputs,
        string outputRoot,
        string scope,
        IReadOnlyList<AssetManifestEntry> scopedAssets)
    {
        foreach (var partition in scopedAssets
                         .GroupBy(GetPartition, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var partitionName = CodeNames.ToPascalCase(partition.Key);
            var chunks = BuildStableChunks(partition);
            for (var index = 0; index < chunks.Length; index++)
            {
                var chunk = chunks[index];
                var output = Path.Combine(
                    outputRoot,
                    $"ResCatalog.{scope}.{partitionName}.B{chunk.Bucket:X1}.{chunk.Index:000}.g.cs");
                outputs.Add(output, BuildAssetPart(chunk.Assets));
            }
        }

        foreach (var group in scopedAssets
                         .Where(asset => !string.IsNullOrWhiteSpace(asset.Group))
                         .GroupBy(asset => asset.Group!, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            AddGroupOutputs(outputs, outputRoot, scope, group.Key, group.ToArray());
        }
    }

    private static string BuildAssetPart(IEnumerable<AssetManifestEntry> assets)
    {
        var builder = BeginCatalogFile();
        builder.AppendLine("public static partial class ResCatalog");
        builder.AppendLine("{");
        foreach (var asset in assets.OrderBy(asset => asset.Id, StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"    public static readonly AssetRef<{asset.ResourceType}> {CodeNames.ToPascalCase(asset.Id)} = new(");
            builder.AppendLine($"        \"{Escape(asset.Path)}\",");
            builder.AppendLine($"        AssetCachePolicy.{asset.CachePolicy});");
            builder.AppendLine();
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AddGroupOutputs(
        IDictionary<string, string> outputs,
        string outputRoot,
        string scope,
        string groupId,
        IReadOnlyList<AssetManifestEntry> assets)
    {
        var groupName = CodeNames.ToPascalCase(groupId);
        var chunks = BuildStableChunks(assets);
        var indexBuilder = BeginCatalogFile();
        indexBuilder.AppendLine("public static partial class ResCatalog");
        indexBuilder.AppendLine("{");
        indexBuilder.AppendLine("    public static partial class Groups");
        indexBuilder.AppendLine("    {");
        indexBuilder.AppendLine(
            $"        public static readonly AssetGroup<{assets[0].ResourceType}> {groupName} = new(");
        indexBuilder.AppendLine($"            Build{groupName}Entries());");
        indexBuilder.AppendLine();
        indexBuilder.AppendLine(
            $"        private static (string Id, AssetRef<{assets[0].ResourceType}> Asset)[] Build{groupName}Entries() =>");
        indexBuilder.AppendLine("        [");
        foreach (var chunk in chunks)
        {
            indexBuilder.AppendLine(
                $"            .. Build{groupName}EntriesB{chunk.Bucket:X1}_{chunk.Index:000}(),");
        }
        indexBuilder.AppendLine("        ];");
        indexBuilder.AppendLine("    }");
        indexBuilder.AppendLine("}");
        outputs.Add(
            Path.Combine(outputRoot, $"ResCatalog.{scope}.Group.{groupName}.g.cs"),
            indexBuilder.ToString());

        foreach (var chunk in chunks)
        {
            var partBuilder = BeginCatalogFile();
            partBuilder.AppendLine("public static partial class ResCatalog");
            partBuilder.AppendLine("{");
            partBuilder.AppendLine("    public static partial class Groups");
            partBuilder.AppendLine("    {");
            partBuilder.AppendLine(
                $"        private static (string Id, AssetRef<{assets[0].ResourceType}> Asset)[] " +
                $"Build{groupName}EntriesB{chunk.Bucket:X1}_{chunk.Index:000}() =>");
            partBuilder.AppendLine("        [");
            foreach (var asset in chunk.Assets)
            {
                partBuilder.AppendLine(
                    $"            (\"{Escape(asset.Id)}\", {CodeNames.ToPascalCase(asset.Id)}),");
            }
            partBuilder.AppendLine("        ];");
            partBuilder.AppendLine("    }");
            partBuilder.AppendLine("}");
            outputs.Add(
                Path.Combine(
                    outputRoot,
                    $"ResCatalog.{scope}.Group.{groupName}.B{chunk.Bucket:X1}.{chunk.Index:000}.g.cs"),
                partBuilder.ToString());
        }
    }

    private static ResourceChunk[] BuildStableChunks(IEnumerable<AssetManifestEntry> assets) =>
        assets
            .GroupBy(asset => StableBucket(asset.Id))
            .OrderBy(group => group.Key)
            .SelectMany(group => group
                .OrderBy(asset => asset.Id, StringComparer.Ordinal)
                .Chunk(EntriesPerFile)
                .Select((chunk, index) => new ResourceChunk(group.Key, index, chunk)))
            .ToArray();

    private static int StableBucket(string id) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(id))[0] & 0x0F;

    private static StringBuilder BeginCatalogFile()
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated by LXFramework.Tools />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("using LX.Res;");
        builder.AppendLine("using Godot;");
        builder.AppendLine();
        builder.AppendLine("namespace LX.Generated;");
        builder.AppendLine();
        return builder;
    }

    private static string GetPartition(AssetManifestEntry asset) =>
        !string.IsNullOrWhiteSpace(asset.CatalogPartition)
            ? asset.CatalogPartition!
            : !string.IsNullOrWhiteSpace(asset.Group)
                ? asset.Group!
                : "default";

    private static void Validate(string root, AssetManifestEntry asset)
    {
        ManifestScopes.Require(asset.Scope, "Resource", asset.Id);
        CodeNames.RequireSnakeCase(asset.Id, nameof(asset.Id));
        if (!ResourceTypeRegex().IsMatch(asset.ResourceType))
        {
            throw new InvalidDataException($"Resource '{asset.Id}' has invalid resource type '{asset.ResourceType}'.");
        }

        if (asset.CachePolicy is not ("Transient" or "Cached" or "Resident"))
        {
            throw new InvalidDataException($"Resource '{asset.Id}' has invalid cache policy '{asset.CachePolicy}'.");
        }
        if (asset.Group is not null)
        {
            CodeNames.RequireSnakeCase(asset.Group, nameof(asset.Group));
        }
        if (asset.CatalogPartition is not null)
        {
            CodeNames.RequireSnakeCase(asset.CatalogPartition, nameof(asset.CatalogPartition));
        }

        var path = ToolFiles.ToAbsolutePath(root, asset.Path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Resource '{asset.Id}' points to missing path '{asset.Path}'.", path);
        }
    }

    private static void ValidatePartitions(AssetManifest manifest)
    {
        var collision = manifest.Assets
            .Select(asset => (asset.Scope, Partition: GetPartition(asset)))
            .Distinct()
            .GroupBy(
                value => (value.Scope, Generated: CodeNames.ToPascalCase(value.Partition)))
            .FirstOrDefault(group => group.Select(value => value.Partition).Distinct(StringComparer.Ordinal).Count() > 1);
        if (collision is not null)
        {
            throw new InvalidDataException(
                $"Resource catalog partitions in scope '{collision.Key.Scope}' collide as generated name " +
                $"'{collision.Key.Generated}'.");
        }
    }

    private static void ValidateGroups(AssetManifest manifest)
    {
        foreach (var group in manifest.Assets
                     .Where(asset => !string.IsNullOrWhiteSpace(asset.Group))
                     .GroupBy(asset => asset.Group!, StringComparer.Ordinal))
        {
            var resourceTypes = group
                .Select(asset => asset.ResourceType)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (resourceTypes.Length != 1)
            {
                throw new InvalidDataException(
                    $"Resource group '{group.Key}' mixes resource types: {string.Join(", ", resourceTypes)}.");
            }
            var scopes = group.Select(asset => asset.Scope).Distinct(StringComparer.Ordinal).ToArray();
            if (scopes.Length != 1)
            {
                throw new InvalidDataException(
                    $"Resource group '{group.Key}' cannot cross Framework and Product scopes.");
            }
        }

        var generatedNameCollision = manifest.Assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Group))
            .Select(asset => asset.Group!)
            .Distinct(StringComparer.Ordinal)
            .GroupBy(CodeNames.ToPascalCase, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (generatedNameCollision is not null)
        {
            throw new InvalidDataException(
                $"Resource groups collide as generated name '{generatedNameCollision.Key}'.");
        }
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceTypeRegex();

    private sealed record ResourceChunk(
        int Bucket,
        int Index,
        AssetManifestEntry[] Assets);
}
