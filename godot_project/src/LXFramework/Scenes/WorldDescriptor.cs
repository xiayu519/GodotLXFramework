namespace LX.Scenes;

public readonly record struct WorldId
{
    public WorldId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("World IDs cannot be empty.", nameof(value))
            : value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record WorldDescriptor(WorldId Id, string ScenePath)
{
    public WorldDescriptor Validate()
    {
        if (string.IsNullOrWhiteSpace(Id.Value))
        {
            throw new ArgumentException("World IDs cannot be empty.", nameof(Id));
        }
        if (string.IsNullOrWhiteSpace(ScenePath) ||
            !ScenePath.StartsWith("res://", StringComparison.Ordinal) ||
            !ScenePath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"World '{Id}' must reference a res:// .tscn scene.",
                nameof(ScenePath));
        }

        return this;
    }
}
