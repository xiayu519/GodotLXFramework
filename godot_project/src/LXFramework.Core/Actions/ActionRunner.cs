using LX.Core.Lifetime;

namespace LX.Core.Actions;

/// <summary>动作节点的终结或执行状态。</summary>
public enum ActionNodeState
{
    /// <summary>节点已经创建但尚未开始。</summary>
    Pending,

    /// <summary>节点正在执行。</summary>
    Running,

    /// <summary>节点成功完成。</summary>
    Completed,

    /// <summary>节点因所属生命周期或调用方取消而结束。</summary>
    Cancelled,

    /// <summary>节点因未处理异常而失败。</summary>
    Failed,
}

/// <summary>动作树中一个节点的不可变诊断快照。</summary>
public sealed record ActionNodeSnapshot(
    long Id,
    string Name,
    ActionNodeState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Error,
    IReadOnlyList<ActionNodeSnapshot> Children);

/// <summary>动作服务当前活动根和最近终结根的有界快照。</summary>
public sealed record ActionRunnerSnapshot(
    IReadOnlyList<ActionNodeSnapshot> Active,
    IReadOnlyList<ActionNodeSnapshot> Recent);

internal sealed class ActionExecutionNode(long id, string name)
{
    private readonly object _gate = new();
    private readonly List<ActionExecutionNode> _children = [];
    private ActionNodeState _state = ActionNodeState.Pending;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private string? _error;

    public long Id { get; } = id;

    public string Name { get; } = name;

    public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;

    public void AddChild(ActionExecutionNode child)
    {
        lock (_gate)
        {
            _children.Add(child);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_state != ActionNodeState.Pending)
            {
                throw new InvalidOperationException($"Action node '{Name}' has already started.");
            }

            _state = ActionNodeState.Running;
            _startedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void Complete(ActionNodeState state, Exception? exception = null)
    {
        if (state is not (ActionNodeState.Completed or ActionNodeState.Cancelled or ActionNodeState.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        lock (_gate)
        {
            _state = state;
            _completedAtUtc = DateTimeOffset.UtcNow;
            _error = exception?.ToString();
        }
    }

    public ActionNodeSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ActionNodeSnapshot(
                Id,
                Name,
                _state,
                CreatedAtUtc,
                _startedAtUtc,
                _completedAtUtc,
                _error,
                _children.Select(child => child.Snapshot()).ToArray());
        }
    }
}

/// <summary>
/// 执行由 <see cref="LXActions"/> 创建的动作树，并保留有界诊断快照。
/// 每次执行同时观察服务、调用方 owner 和显式取消令牌。
/// </summary>
public sealed class ActionRunner : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown;
    private readonly CancellationToken _shutdownToken;
    private readonly Dictionary<long, (ActionExecutionNode Node, Task Task)> _active = [];
    private readonly Queue<ActionExecutionNode> _recent = [];
    private readonly int _recentCapacity;
    private long _nextId;
    private TaskCompletionSource<object?>? _shutdownCompletion;
    private bool _disposed;

    internal CancellationToken ShutdownToken => _shutdownToken;

    /// <summary>创建动作服务，并把所有执行绑定到给定父生命周期。</summary>
    public ActionRunner(LifetimeScope parentLifetime, int recentCapacity = 32)
    {
        ArgumentNullException.ThrowIfNull(parentLifetime);
        if (recentCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recentCapacity));
        }

        _shutdown = CancellationTokenSource.CreateLinkedTokenSource(parentLifetime.Token);
        _shutdownToken = _shutdown.Token;
        _recentCapacity = recentCapacity;
    }

    /// <summary>
    /// 开始执行一个动作树并返回其完成任务；动作会在本方法返回前执行到首次未完成等待。
    /// 取消任一所属令牌都会取消本次执行；与所属令牌无关的取消异常按失败处理。
    /// </summary>
    public Task RunAsync(
        LXAction action,
        LifetimeScope owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(owner);
        owner.ThrowIfDisposed();

        var root = new ActionExecutionNode(NextId(), action.Name);
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource operation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            operation = CancellationTokenSource.CreateLinkedTokenSource(
                _shutdownToken,
                owner.Token,
                cancellationToken);
            _active.Add(root.Id, (root, completion.Task));
        }

        _ = RunRootAsync(root, action, operation, completion);
        return completion.Task;
    }

    /// <summary>返回当前活动动作和最近终结动作的稳定快照。</summary>
    public ActionRunnerSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ActionRunnerSnapshot(
                _active.Values.Select(entry => entry.Node.Snapshot()).ToArray(),
                _recent.Select(node => node.Snapshot()).ToArray());
        }
    }

    public void Dispose()
    {
        _ = BeginShutdown();
    }

    public ValueTask DisposeAsync() => new(BeginShutdown());

    private Task BeginShutdown()
    {
        TaskCompletionSource<object?> completion;
        Task[] active;
        lock (_gate)
        {
            if (_shutdownCompletion is not null)
            {
                return _shutdownCompletion.Task;
            }

            _disposed = true;
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdownCompletion = completion;
            active = _active.Values.Select(entry => entry.Task).ToArray();
        }

        Exception? cancellationError = null;
        try
        {
            _shutdown.Cancel();
        }
        catch (Exception exception)
        {
            cancellationError = exception;
        }
        _ = CompleteShutdownAsync(active, cancellationError, completion);
        return completion.Task;
    }

    private async Task CompleteShutdownAsync(
        IReadOnlyList<Task> active,
        Exception? cancellationError,
        TaskCompletionSource<object?> completion)
    {
        try
        {
            try
            {
                await Task.WhenAll(active);
            }
            catch
            {
                // Callers observe execution errors. Shutdown only guarantees termination.
            }

            _shutdown.Dispose();
            if (cancellationError is null)
            {
                completion.TrySetResult(null);
            }
            else
            {
                completion.TrySetException(cancellationError);
            }
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    internal async ValueTask ExecuteChildAsync(
        ActionExecutionNode parent,
        LXAction action,
        CancellationToken cancellationToken)
    {
        var child = new ActionExecutionNode(NextId(), action.Name);
        parent.AddChild(child);
        await ExecuteNodeAsync(child, action, cancellationToken);
    }

    private async Task RunRootAsync(
        ActionExecutionNode root,
        LXAction action,
        CancellationTokenSource operation,
        TaskCompletionSource<object?> completion)
    {
        Exception? failure = null;
        var cancelled = false;
        CancellationToken cancelledToken = default;
        using (operation)
        {
            try
            {
                await ExecuteNodeAsync(root, action, operation.Token);
            }
            catch (OperationCanceledException exception) when (operation.Token.IsCancellationRequested)
            {
                cancelled = true;
                cancelledToken = exception.CancellationToken.CanBeCanceled
                    ? exception.CancellationToken
                    : operation.Token;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                lock (_gate)
                {
                    _active.Remove(root.Id);
                    _recent.Enqueue(root);
                    while (_recent.Count > _recentCapacity)
                    {
                        _recent.Dequeue();
                    }
                }
            }
        }

        if (failure is not null)
        {
            completion.TrySetException(failure);
        }
        else if (cancelled)
        {
            completion.TrySetCanceled(cancelledToken);
        }
        else
        {
            completion.TrySetResult(null);
        }
    }

    private async ValueTask ExecuteNodeAsync(
        ActionExecutionNode node,
        LXAction action,
        CancellationToken cancellationToken)
    {
        node.Start();
        try
        {
            await action.ExecuteAsync(new ActionExecutionContext(this, node, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            node.Complete(ActionNodeState.Completed);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            node.Complete(ActionNodeState.Cancelled, exception);
            throw;
        }
        catch (Exception exception)
        {
            node.Complete(ActionNodeState.Failed, exception);
            throw;
        }
    }

    private long NextId() => Interlocked.Increment(ref _nextId);
}
