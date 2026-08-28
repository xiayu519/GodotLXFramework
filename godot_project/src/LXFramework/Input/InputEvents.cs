using Godot;

namespace LX.Input;

/// <summary>最近一次有效用户输入所属的设备类别，用于提示图标和交互模式切换。</summary>
public enum InputModality
{
    /// <summary>键盘按键或鼠标事件。</summary>
    MouseKeyboard,

    /// <summary>游戏手柄按钮、摇杆或扳机事件。</summary>
    Gamepad,

    /// <summary>触摸屏按压或拖动事件。</summary>
    Touch,
}

public readonly record struct InputModalityChanged(InputModality Previous, InputModality Current);

public readonly record struct GameActionTriggered(
    InputActionId Action,
    bool Pressed,
    float Strength,
    InputModality Modality);

/// <summary>统一指针事件在单次交互中的阶段。</summary>
public enum PointerPhase
{
    /// <summary>指针位置变化但未产生新的按键状态。</summary>
    Moved,

    /// <summary>某个指针按钮刚被按下。</summary>
    Pressed,

    /// <summary>某个指针按钮刚被释放。</summary>
    Released,

    /// <summary>鼠标滚轮或等价滚动轴发生变化。</summary>
    Wheel,
}

public readonly record struct PointerInput(
    PointerPhase Phase,
    Vector2 ViewportPosition,
    MouseButton Button,
    float WheelDelta,
    bool Shift,
    bool Ctrl,
    bool Alt);

public readonly record struct InputBindingChanged(string GodotAction);
