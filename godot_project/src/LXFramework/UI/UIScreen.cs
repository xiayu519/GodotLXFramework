using LX.Core.Lifetime;
using LX.Runtime;
using Godot;

namespace LX.UI;

public abstract partial class UIScreen : Control, ILXContextReceiver
{
    private LXContext? _context;
    private LifetimeScope? _lifetime;
    private LifetimeScope? _activation;

    internal event Action<UICompletion>? CloseRequested;

    protected LifetimeScope Activation => _activation ??
        throw new InvalidOperationException("The UI screen is not currently active.");

    protected LXContext LX => _context ??
        throw new InvalidOperationException($"{GetType().Name} has not received a LXFramework context.");

    protected LifetimeScope Lifetime => _lifetime ??
        throw new InvalidOperationException($"{GetType().Name} has not received a LXFramework lifetime.");

    public bool IsLXInitialized => _context is not null;

    public void Initialize(LXContext context, LifetimeScope lifetime)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lifetime);
        if (_context is not null)
        {
            throw new InvalidOperationException($"{GetType().Name} was initialized more than once.");
        }

        _context = context;
        _lifetime = lifetime;
        OnLXInitialized();
    }

    public sealed override void _Ready()
    {
        BindGeneratedNodes();
        OnBindingsReady();
    }

    protected virtual void BindGeneratedNodes()
    {
    }

    protected virtual void OnBindingsReady()
    {
    }

    protected virtual void OnLXInitialized()
    {
    }

    protected internal virtual ValueTask OnShowAsync(object? payload, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    protected internal virtual ValueTask OnHideAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <summary>页面进入或退出时调用；实现应观察取消令牌并避免永久阻塞关闭。</summary>
    protected internal virtual ValueTask OnTransitionAsync(
        UITransitionPhase phase,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// 语义 Cancel 动作触发时，由最上层弹窗或页面调用。
    /// 返回 true 允许 UIService 关闭当前页面。
    /// </summary>
    protected internal virtual ValueTask<bool> OnBackRequestedAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    /// <summary>请求关闭页面且不返回结果。</summary>
    protected void RequestClose() => CloseRequested?.Invoke(UICompletion.Cancelled);

    /// <summary>请求关闭页面并向调用者返回一个强类型结果。</summary>
    protected void RequestClose<TResult>(TResult result) =>
        CloseRequested?.Invoke(new UICompletion(true, result));

    /// <summary>
    /// 为当前页面激活期绑定动态纹理；页面隐藏、关闭或重新打开前会先清空控件再释放租约。
    /// </summary>
    protected UITextureBinding BindTexture(TextureRect target) =>
        UITextureBinding.Create(LX.Res, Activation, target);

    /// <summary>
    /// 为当前页面激活期绑定动态九宫格纹理；AtlasTexture 也使用此入口。
    /// </summary>
    protected UITextureBinding BindTexture(NinePatchRect target) =>
        UITextureBinding.Create(LX.Res, Activation, target);

    /// <summary>为当前页面激活期绑定 TextureButton 的指定状态纹理。</summary>
    protected UITextureBinding BindTexture(TextureButton target, TextureButtonSlot slot) =>
        UITextureBinding.Create(LX.Res, Activation, target, slot);

    internal void SetActivation(LifetimeScope? activation) => _activation = activation;
}

/// <summary>页面过渡回调的方向。</summary>
public enum UITransitionPhase
{
    /// <summary>页面已加入 UI 树，正在进入可交互状态。</summary>
    Entering,
    /// <summary>页面即将隐藏或销毁。</summary>
    Exiting,
}
