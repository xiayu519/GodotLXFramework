namespace LX.Res;

/// <summary>Godot 资源路径的规范形式检查，避免同一资源以多个字典键进入框架。</summary>
internal static class GodotResourcePath
{
    internal static bool IsCanonical(string? path, string? requiredExtension = null)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("res://", StringComparison.Ordinal) ||
            path.Contains('\\'))
        {
            return false;
        }

        var relative = path["res://".Length..];
        if (relative.Length == 0 ||
            relative.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            return false;
        }

        return requiredExtension is null ||
               path.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase);
    }

    internal static void Validate(string? path, string parameterName)
    {
        if (!IsCanonical(path))
        {
            throw new ArgumentException(
                "Asset paths must be canonical non-empty res:// paths without traversal or duplicate separators.",
                parameterName);
        }
    }
}
