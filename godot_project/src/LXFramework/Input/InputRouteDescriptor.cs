using Godot;

namespace LX.Input;

public sealed record InputRouteDescriptor(
    InputActionId Action,
    string GodotAction,
    Key? DefaultPhysicalKey = null)
{
    public InputRouteDescriptor Validate()
    {
        if (string.IsNullOrWhiteSpace(Action.Value))
        {
            throw new ArgumentException("Input action IDs cannot be empty.", nameof(Action));
        }
        if (string.IsNullOrWhiteSpace(GodotAction))
        {
            throw new ArgumentException("Godot input action names cannot be empty.", nameof(GodotAction));
        }
        if (DefaultPhysicalKey == Key.None)
        {
            throw new ArgumentException("Default physical keys cannot be Key.None.", nameof(DefaultPhysicalKey));
        }

        return this;
    }
}
