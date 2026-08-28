namespace LX.Content;

public readonly record struct ContentRef<T>
{
    public ContentRef(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("res://content/", StringComparison.Ordinal) ||
            !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Content references must use a res://content/*.json path.",
                nameof(path));
        }

        Path = path;
    }

    public string Path { get; }
}
