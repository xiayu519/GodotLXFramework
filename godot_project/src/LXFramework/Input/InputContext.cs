using Godot;

namespace LX.Input;

/// <summary>输入上下文如何处理未显式列出的动作。</summary>
public enum InputContextMode
{
    /// <summary>未列出的动作继续交给下层上下文处理。</summary>
    Passthrough,

    /// <summary>未列出的动作被当前上下文拦截，适合菜单、对话框和暂停界面。</summary>
    Exclusive,
}

/// <summary>一组临时生效的输入动作及其拦截策略。</summary>
public sealed record InputContextDescriptor(
    string Id,
    IReadOnlySet<InputActionId> Actions,
    InputContextMode Mode = InputContextMode.Exclusive)
{
    /// <summary>校验上下文 ID、动作集合和枚举值。</summary>
    public InputContextDescriptor Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("Input context IDs cannot be empty.", nameof(Id));
        }
        ArgumentNullException.ThrowIfNull(Actions);
        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(Mode));
        }
        return this;
    }
}

/// <summary>输入上下文栈中的一项只读快照。</summary>
public sealed record InputContextRecord(
    string Id,
    InputContextMode Mode,
    IReadOnlyList<string> Actions,
    int Order);

/// <summary>一个动作当前绑定的可读提示。</summary>
public sealed record InputPrompt(
    InputActionId Action,
    InputModality Modality,
    string Text);

/// <summary>两个 Godot 输入动作共享同一物理按键时产生的冲突。</summary>
public sealed record InputBindingConflict(
    Key PhysicalKey,
    IReadOnlyList<string> GodotActions);

/// <summary>输入路由器当前模态、上下文与绑定冲突的可序列化快照。</summary>
public sealed record InputSnapshot(
    InputModality Modality,
    IReadOnlyList<InputContextRecord> Contexts,
    IReadOnlyList<InputBindingConflict> Conflicts);

/// <summary>控制一个输入上下文的生存期；释放后会从路由栈中移除该上下文。</summary>
public sealed class InputContextHandle : IDisposable
{
    private InputRouter? _owner;
    private readonly long _token;

    internal InputContextHandle(InputRouter owner, long token)
    {
        _owner = owner;
        _token = token;
    }

    /// <summary>该上下文是否已经被移除。</summary>
    public bool IsDisposed => _owner is null;

    /// <summary>从输入栈中移除对应上下文；重复调用没有副作用。</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _owner, null)?.RemoveContext(_token);
    }
}
