namespace LX.UI;

/// <summary>UI 页面在全局画布中的稳定层级。</summary>
public enum UILayer
{
    /// <summary>主页面层，同一时刻通常只有一个主要页面可交互。</summary>
    Screen,
    /// <summary>弹窗层，位于主页面之上。</summary>
    Popup,
    /// <summary>全局覆盖层，适合 Toast、加载提示和调试信息。</summary>
    Overlay,
}

/// <summary>页面关闭后的实例缓存策略。</summary>
public enum UICachePolicy
{
    /// <summary>关闭时销毁节点和资源租约，下次打开重新实例化。</summary>
    Transient,
    /// <summary>全局只允许一个实例，关闭后隐藏并缓存以便复用。</summary>
    CachedSingleton,
}

/// <summary>新页面打开时如何处理同层较早页面。</summary>
public enum UICoverPolicy
{
    /// <summary>旧页面保持显示，适合透明覆盖层。</summary>
    KeepVisible,
    /// <summary>旧页面隐藏并暂停处理，关闭新页面后恢复。</summary>
    HidePrevious,
    /// <summary>打开新页面前关闭同层所有旧页面。</summary>
    ClosePrevious,
}

/// <summary>页面是否拦截指针输入。</summary>
public enum UIInputPolicy
{
    /// <summary>使用页面节点自身的 MouseFilter 设置。</summary>
    Normal,
    /// <summary>页面根节点拦截指针输入，适合必须先处理的对话框。</summary>
    Modal,
}

/// <summary>页面显示后的键盘/手柄焦点策略。</summary>
public enum UIFocusPolicy
{
    /// <summary>不改变当前焦点。</summary>
    Preserve,
    /// <summary>自动聚焦页面树中第一个可聚焦且可见的控件。</summary>
    GrabFirst,
}

/// <summary>页面在 UI 栈中的当前可见状态。</summary>
public enum UIVisualState
{
    /// <summary>页面当前可见并可处理输入。</summary>
    Visible,
    /// <summary>页面被同层较新的页面覆盖。</summary>
    Covered,
}

/// <summary>UI 清单生成的页面注册描述。</summary>
public sealed record UIDescriptor(
    UIId Id,
    string ScenePath,
    UILayer Layer = UILayer.Screen,
    UICachePolicy CachePolicy = UICachePolicy.Transient,
    UICoverPolicy CoverPolicy = UICoverPolicy.KeepVisible,
    UIInputPolicy InputPolicy = UIInputPolicy.Normal,
    UIFocusPolicy FocusPolicy = UIFocusPolicy.Preserve);
