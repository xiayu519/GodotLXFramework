namespace LX.UI;

public readonly record struct UIId
{
    public UIId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("UI IDs cannot be empty.", nameof(value))
            : value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
