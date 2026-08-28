namespace LX.Core.Common;

public readonly record struct StrongId<TTag> : IComparable<StrongId<TTag>>
{
    public StrongId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A strong ID cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public int CompareTo(StrongId<TTag> other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;
}
