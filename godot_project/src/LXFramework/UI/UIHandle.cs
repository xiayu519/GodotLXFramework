namespace LX.UI;

public sealed class UIHandle : IAsyncDisposable
{
    private UIService? _owner;
    private readonly Guid _instanceId;
    private readonly Task<UICompletion> _completion;

    internal UIHandle(
        UIService owner,
        Guid instanceId,
        UIId uiId,
        Task<UICompletion> completion)
    {
        _owner = owner;
        _instanceId = instanceId;
        UIId = uiId;
        _completion = completion;
    }

    public UIId UIId { get; }

    public bool IsClosed => _owner is null;

    /// <summary>等待页面关闭并把结果转换为调用方指定类型。</summary>
    public async ValueTask<UIResult<TResult>> WaitForResultAsync<TResult>(
        CancellationToken cancellationToken = default)
    {
        var completion = await _completion.WaitAsync(cancellationToken);
        if (!completion.HasValue)
        {
            return new UIResult<TResult>(false, default);
        }
        if (completion.Value is null)
        {
            return new UIResult<TResult>(true, default);
        }
        if (completion.Value is not TResult typed)
        {
            throw new InvalidCastException(
                $"UI '{UIId}' returned {completion.Value.GetType().Name}, not {typeof(TResult).Name}.");
        }
        return new UIResult<TResult>(true, typed);
    }

    public async ValueTask CloseAsync()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is not null)
        {
            await owner.CloseAsync(_instanceId);
        }
    }

    public ValueTask DisposeAsync() => CloseAsync();
}

/// <summary>页面是否返回了值以及对应的强类型结果。</summary>
public readonly record struct UIResult<TResult>(bool HasValue, TResult? Value);

/// <summary>UIService 内部和页面之间传递的关闭结果。</summary>
public readonly record struct UICompletion(bool HasValue, object? Value)
{
    /// <summary>表示页面被取消、返回或外部生命周期关闭，没有返回值。</summary>
    public static UICompletion Cancelled => new(false, null);
}
