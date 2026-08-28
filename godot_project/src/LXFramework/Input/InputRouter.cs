using LX.Core.Events;
using LX.Generated;
using Godot;

namespace LX.Input;

public sealed class InputRouter
{
    private sealed record ActiveContext(long Token, InputContextDescriptor Descriptor);

    private readonly EventHub _events;
    private readonly Dictionary<StringName, InputActionId> _actions = [];
    private readonly Dictionary<InputActionId, List<StringName>> _routes = [];
    private readonly List<ActiveContext> _contexts = [];
    private readonly int _mainThreadId;
    private long _contextSequence;

    public InputRouter(EventHub events)
        : this(events, InputCatalog.All)
    {
    }

    public InputRouter(EventHub events, IEnumerable<InputRouteDescriptor> routes)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        ArgumentNullException.ThrowIfNull(routes);
        foreach (var route in routes)
        {
            route.Validate();
            EnsureAction(route.GodotAction, route.DefaultPhysicalKey);
            Register(route.GodotAction, route.Action);
        }
    }

    public InputModality CurrentModality { get; private set; } = InputModality.MouseKeyboard;

    /// <summary>
    /// 把输入上下文压入栈顶。Exclusive 上下文会阻止未列出的动作进入游戏层。
    /// </summary>
    public InputContextHandle PushContext(InputContextDescriptor descriptor)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.Validate();
        var token = ++_contextSequence;
        _contexts.Add(new ActiveContext(token, descriptor));
        return new InputContextHandle(this, token);
    }

    public void Register(StringName godotAction, InputActionId action)
    {
        EnsureMainThread();
        if (godotAction.IsEmpty)
        {
            throw new ArgumentException("Godot action names cannot be empty.", nameof(godotAction));
        }

        if (_actions.TryGetValue(godotAction, out var existing))
        {
            if (existing != action)
            {
                throw new InvalidOperationException(
                    $"Godot action '{godotAction}' is already mapped to '{existing}'.");
            }

            return;
        }

        _actions.Add(godotAction, action);
        if (!_routes.TryGetValue(action, out var routes))
        {
            routes = [];
            _routes.Add(action, routes);
        }
        if (!routes.Contains(godotAction))
        {
            routes.Add(godotAction);
        }
    }

    public bool IsPressed(InputActionId action)
    {
        EnsureMainThread();
        return IsActionEnabled(action) &&
               _routes.TryGetValue(action, out var routes) &&
               routes.Any(route => Godot.Input.IsActionPressed(route));
    }

    public float Strength(InputActionId action)
    {
        EnsureMainThread();
        return StrengthCore(action);
    }

    private float StrengthCore(InputActionId action) =>
        IsActionEnabled(action) && _routes.TryGetValue(action, out var routes)
            ? routes.Max(route => Godot.Input.GetActionStrength(route))
            : 0;

    /// <summary>返回动作在当前输入设备下最合适的可读按键提示。</summary>
    public InputPrompt Prompt(InputActionId action)
    {
        EnsureMainThread();
        if (!_routes.TryGetValue(action, out var routes))
        {
            return new InputPrompt(action, CurrentModality, action.Value);
        }

        var candidates = routes
            .SelectMany(route => InputMap.ActionGetEvents(route))
            .ToArray();
        var selected = CurrentModality switch
        {
            InputModality.Gamepad => candidates.FirstOrDefault(inputEvent =>
                inputEvent is InputEventJoypadButton or InputEventJoypadMotion),
            InputModality.Touch => candidates.FirstOrDefault(inputEvent =>
                inputEvent is InputEventScreenTouch or InputEventScreenDrag),
            _ => candidates.FirstOrDefault(inputEvent =>
                inputEvent is InputEventKey or InputEventMouseButton),
        } ?? candidates.FirstOrDefault();
        return new InputPrompt(action, CurrentModality, selected?.AsText() ?? action.Value);
    }

    /// <summary>查找多个 Godot 动作共享同一物理键的绑定冲突。</summary>
    public IReadOnlyList<InputBindingConflict> FindBindingConflicts()
    {
        EnsureMainThread();
        return _actions.Keys
            .SelectMany(action => InputMap.ActionGetEvents(action)
                .OfType<InputEventKey>()
                .Where(inputEvent => inputEvent.PhysicalKeycode != Key.None)
                .Select(inputEvent => (Action: action.ToString(), inputEvent.PhysicalKeycode)))
            .GroupBy(binding => binding.PhysicalKeycode)
            .Where(group => group.Select(binding => binding.Action).Distinct(StringComparer.Ordinal).Count() > 1)
            .OrderBy(group => group.Key)
            .Select(group => new InputBindingConflict(
                group.Key,
                group.Select(binding => binding.Action)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    /// <summary>返回当前输入模态、上下文栈和按键冲突。</summary>
    public InputSnapshot Snapshot()
    {
        EnsureMainThread();
        return new InputSnapshot(
            CurrentModality,
            _contexts.Select((context, index) => new InputContextRecord(
                    context.Descriptor.Id,
                    context.Descriptor.Mode,
                    context.Descriptor.Actions.Select(action => action.Value)
                        .OrderBy(action => action, StringComparer.Ordinal)
                        .ToArray(),
                    index))
                .ToArray(),
            FindBindingConflicts());
    }

    public Vector2 Direction(
        InputActionId negativeX,
        InputActionId positiveX,
        InputActionId negativeY,
        InputActionId positiveY)
    {
        EnsureMainThread();
        var direction = new Vector2(
            StrengthCore(positiveX) - StrengthCore(negativeX),
            StrengthCore(positiveY) - StrengthCore(negativeY));
        return direction.LengthSquared() > 1 ? direction.Normalized() : direction;
    }

    public bool HasGodotAction(StringName godotAction)
    {
        EnsureMainThread();
        return _actions.ContainsKey(godotAction);
    }

    public void ReplaceKeyBinding(StringName godotAction, Key physicalKey)
    {
        EnsureMainThread();
        if (physicalKey == Key.None)
        {
            throw new ArgumentException("Physical key bindings cannot use Key.None.", nameof(physicalKey));
        }
        if (!_actions.ContainsKey(godotAction))
        {
            throw new KeyNotFoundException($"Godot input action '{godotAction}' is not registered.");
        }

        foreach (var inputEvent in InputMap.ActionGetEvents(godotAction).ToArray())
        {
            if (inputEvent is InputEventKey)
            {
                InputMap.ActionEraseEvent(godotAction, inputEvent);
            }
        }
        InputMap.ActionAddEvent(godotAction, new InputEventKey { PhysicalKeycode = physicalKey });
        _events.Publish(new InputBindingChanged(godotAction.ToString()));
    }

    public void Handle(InputEvent inputEvent)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(inputEvent);
        UpdateModality(inputEvent);

        foreach (var pair in _actions)
        {
            if (!IsActionEnabled(pair.Value))
            {
                continue;
            }
            if (inputEvent.IsActionPressed(pair.Key, allowEcho: false))
            {
                _events.Publish(new GameActionTriggered(
                    pair.Value,
                    true,
                    inputEvent.GetActionStrength(pair.Key),
                    CurrentModality));
            }
            else if (inputEvent.IsActionReleased(pair.Key))
            {
                _events.Publish(new GameActionTriggered(pair.Value, false, 0, CurrentModality));
            }
        }

        switch (inputEvent)
        {
            case InputEventMouseMotion motion:
                PublishPointer(PointerPhase.Moved, motion.Position, MouseButton.None, 0, motion);
                break;
            case InputEventMouseButton button when button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                PublishPointer(
                    PointerPhase.Wheel,
                    button.Position,
                    button.ButtonIndex,
                    button.ButtonIndex == MouseButton.WheelUp ? 1 : -1,
                    button);
                break;
            case InputEventMouseButton button:
                PublishPointer(
                    button.Pressed ? PointerPhase.Pressed : PointerPhase.Released,
                    button.Position,
                    button.ButtonIndex,
                    0,
                    button);
                if (button.ButtonIndex == MouseButton.Right && button.Pressed)
                {
                    _events.Publish(new GameActionTriggered(
                        LXInputActions.Cancel,
                        true,
                        1,
                        CurrentModality));
                }

                break;
            case InputEventScreenDrag drag:
                _events.Publish(new PointerInput(
                    PointerPhase.Moved,
                    drag.Position,
                    MouseButton.Left,
                    0,
                    false,
                    false,
                    false));
                break;
            case InputEventScreenTouch touch:
                _events.Publish(new PointerInput(
                    touch.Pressed ? PointerPhase.Pressed : PointerPhase.Released,
                    touch.Position,
                    MouseButton.Left,
                    0,
                    false,
                    false,
                    false));
                break;
        }
    }

    private void UpdateModality(InputEvent inputEvent)
    {
        var next = inputEvent switch
        {
            InputEventJoypadButton or InputEventJoypadMotion => InputModality.Gamepad,
            InputEventScreenTouch or InputEventScreenDrag => InputModality.Touch,
            _ => InputModality.MouseKeyboard,
        };
        if (next == CurrentModality)
        {
            return;
        }

        var previous = CurrentModality;
        CurrentModality = next;
        _events.Publish(new InputModalityChanged(previous, next));
    }

    internal void RemoveContext(long token)
    {
        EnsureMainThread();
        var index = _contexts.FindLastIndex(context => context.Token == token);
        if (index >= 0)
        {
            _contexts.RemoveAt(index);
        }
    }

    private bool IsActionEnabled(InputActionId action)
    {
        for (var index = _contexts.Count - 1; index >= 0; index--)
        {
            var descriptor = _contexts[index].Descriptor;
            if (descriptor.Actions.Contains(action))
            {
                return true;
            }
            if (descriptor.Mode == InputContextMode.Exclusive)
            {
                return false;
            }
        }
        return true;
    }

    private void PublishPointer(
        PointerPhase phase,
        Vector2 position,
        MouseButton button,
        float wheelDelta,
        InputEventWithModifiers modifiers)
    {
        _events.Publish(new PointerInput(
            phase,
            position,
            button,
            wheelDelta,
            modifiers.ShiftPressed,
            modifiers.CtrlPressed,
            modifiers.AltPressed));
    }

    private static void EnsureAction(StringName action, Key? physicalKey)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        if (physicalKey is not null)
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = physicalKey.Value });
        }
    }

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Input operations must run on Godot's main thread.");
        }
    }
}
