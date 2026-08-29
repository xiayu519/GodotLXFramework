using LX.Res;

namespace LX.Features;

public readonly record struct FeatureId
{
    public FeatureId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Feature IDs cannot be empty.", nameof(value))
            : value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record FeatureDescriptor(FeatureId Id, string ScenePath)
{
    public FeatureDescriptor Validate()
    {
        if (string.IsNullOrWhiteSpace(Id.Value))
        {
            throw new ArgumentException("Feature IDs cannot be empty.", nameof(Id));
        }
        if (!GodotResourcePath.IsCanonical(ScenePath, ".tscn"))
        {
            throw new ArgumentException(
                $"Feature '{Id}' must reference a res:// .tscn scene.",
                nameof(ScenePath));
        }

        return this;
    }
}
