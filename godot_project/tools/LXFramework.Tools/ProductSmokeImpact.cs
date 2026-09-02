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

    private static bool Matches(string pattern, string changedPath)
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
}
