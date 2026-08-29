namespace LX.Core.Actions;

/// <summary>一个可由 <see cref="ActionRunner"/> 执行和观测的不可变动作定义。</summary>
public abstract class LXAction
{
    protected LXAction(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Action names cannot be empty.", nameof(name))
            : name.Trim();
    }

    /// <summary>用于诊断快照的稳定动作名称。</summary>
    public string Name { get; }

    internal abstract ValueTask ExecuteAsync(ActionExecutionContext context);
}

internal sealed class ActionExecutionContext(
    ActionRunner runner,
    ActionExecutionNode node,
    CancellationToken cancellationToken)
{
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public ValueTask RunChildAsync(LXAction action, CancellationToken? overrideToken = null) =>
        runner.ExecuteChildAsync(node, action, overrideToken ?? CancellationToken);

    public ValueTask RunCleanupAsync(LXAction action) =>
        runner.ExecuteChildAsync(node, action, runner.ShutdownToken);
}
