namespace LXFramework.Tools;

internal static class ProductLayout
{
    public static string GetSourceRoot(GameManifest manifest)
    {
        var sourceRoot = manifest.SourceRoot.Replace('\\', '/').Trim('/');
        if (sourceRoot.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new InvalidDataException(
                    "A declared game product must define an explicit workspace-relative sourceRoot.");
            }
            return string.Empty;
        }

        var segments = sourceRoot.Split('/');
        if (Path.IsPathRooted(sourceRoot) ||
            segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
                                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException(
                $"Game source root '{manifest.SourceRoot}' must be a safe workspace-relative path.");
        }

        return string.Join('/', segments);
    }

    public static string GetSourceDirectory(string root, GameManifest manifest)
    {
        var sourceRoot = GetSourceRoot(manifest);
        if (sourceRoot.Length == 0)
        {
            throw new InvalidDataException("A declared game product must define sourceRoot.");
        }

        return Path.Combine(root, sourceRoot.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string GetSourcePath(string root, GameManifest manifest, params string[] segments) =>
        Path.Combine([GetSourceDirectory(root, manifest), .. segments]);

    public static string GetGeneratedDirectory(string root, GameManifest manifest) =>
        GetSourcePath(root, manifest, "Generated");

    public static string GetResourcePath(GameManifest manifest, params string[] segments)
    {
        var suffix = string.Join('/', segments.Select(segment => segment.Replace('\\', '/').Trim('/')));
        return $"res://{GetSourceRoot(manifest)}/{suffix}";
    }

    public static bool IsProductNamespace(GameManifest manifest, string candidate) =>
        !string.IsNullOrWhiteSpace(manifest.Name) &&
        (string.Equals(candidate, manifest.RootNamespace, StringComparison.Ordinal) ||
         candidate.StartsWith(manifest.RootNamespace + ".", StringComparison.Ordinal));
}

internal static class ManifestScopes
{
    public const string Framework = "Framework";
    public const string Product = "Product";

    public static void Require(string scope, string kind, string id)
    {
        if (scope is not (Framework or Product))
        {
            throw new InvalidDataException(
                $"{kind} '{id}' has invalid scope '{scope}'; expected Framework or Product.");
        }
    }
}
