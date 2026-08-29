using LX.Res;

namespace LX.Content;

public readonly record struct ContentRef<T>
{
    public ContentRef(string path)
    {
        if (!GodotResourcePath.IsCanonical(path, ".json") ||
            !path.StartsWith("res://content/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Content references must use a res://content/*.json path.",
                nameof(path));
        }

        Path = path;
    }

    public string Path { get; }
}
