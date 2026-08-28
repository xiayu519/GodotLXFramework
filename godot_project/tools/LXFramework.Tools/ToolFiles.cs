using System.Text.Json;
using System.Text.Json.Serialization;

namespace LXFramework.Tools;

internal static class ToolFiles
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToAbsolutePath(string root, string resourcePath)
    {
        if (!resourcePath.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Expected a res:// path, got '{resourcePath}'.", nameof(resourcePath));
        }

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(
            Path.Combine(fullRoot, resourcePath[6..].Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException(
                $"Resource path '{resourcePath}' resolves outside the project root.",
                nameof(resourcePath));
        }

        return candidate;
    }

    public static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    public static bool WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), normalized, StringComparison.Ordinal))
        {
            return false;
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, normalized, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, true);
        return true;
    }

    public static bool WriteJson<T>(string path, T value) =>
        WriteText(path, JsonSerializer.Serialize(value, JsonOptions) + "\n");

    public static T ReadJson<T>(string path) where T : notnull =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ??
        throw new InvalidDataException($"JSON file '{path}' produced no value.");
}
