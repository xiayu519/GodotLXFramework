using LX.UI;

namespace LX.Validation;

/// <summary>仅供框架 smoke 验证 UI 过渡与强类型结果闭环的页面。</summary>
public partial class UIResultProbeScreen : UIScreen
{
    /// <summary>进入过渡实际执行的次数。</summary>
    public int EnterTransitions { get; private set; }

    /// <summary>退出过渡实际执行的次数。</summary>
    public int ExitTransitions { get; private set; }

    /// <inheritdoc />
    protected internal override ValueTask OnTransitionAsync(
        UITransitionPhase phase,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (phase == UITransitionPhase.Entering)
        {
            EnterTransitions++;
        }
        else
        {
            ExitTransitions++;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>模拟用户确认并向打开者返回结果。</summary>
    public void Complete(string value) => RequestClose(value);
}
