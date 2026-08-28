namespace LX.Input;

public readonly record struct InputActionId
{
    public InputActionId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Input action IDs cannot be empty.", nameof(value))
            : value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
