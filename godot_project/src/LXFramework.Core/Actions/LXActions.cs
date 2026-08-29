namespace LX.Core.Actions;

/// <summary>创建小型、可组合、可取消的动作定义。</summary>
public static class LXActions
{
    /// <summary>按声明顺序执行全部子动作。</summary>
    public static LXAction Sequence(params LXAction[] actions) =>
        new SequenceAction("sequence", ValidateActions(actions));

    /// <summary>并行执行全部子动作；任一失败会取消仍在执行的兄弟动作。</summary>
    public static LXAction Parallel(params LXAction[] actions) =>
        new ParallelAction("parallel", ValidateActions(actions));

    /// <summary>返回最先终结的子动作结果，并取消其他子动作。</summary>
    public static LXAction Race(params LXAction[] actions) =>
        new RaceAction("race", ValidateActions(actions));

    /// <summary>执行一个同步回调。</summary>
    public static LXAction Invoke(Action callback, string name = "invoke") =>
        new InvokeAction(name, callback ?? throw new ArgumentNullException(nameof(callback)));

    /// <summary>执行一个观察取消令牌的异步回调。</summary>
    public static LXAction Async(
        Func<CancellationToken, ValueTask> callback,
        string name = "async") =>
        new AsyncAction(name, callback ?? throw new ArgumentNullException(nameof(callback)));

    /// <summary>等待指定时间；时间必须为非负值。</summary>
    public static LXAction Delay(TimeSpan duration, string name = "delay") =>
        new DelayAction(name, duration);

    /// <summary>限制子动作的最长执行时间。</summary>
    public static LXAction Timeout(LXAction action, TimeSpan timeout, string name = "timeout") =>
        new TimeoutAction(name, action ?? throw new ArgumentNullException(nameof(action)), timeout);

    /// <summary>失败时重新执行子动作，最多执行 <paramref name="maxAttempts"/> 次。</summary>
    public static LXAction Retry(
        LXAction action,
        int maxAttempts,
        TimeSpan? delay = null,
        string name = "retry") =>
        new RetryAction(
            name,
            action ?? throw new ArgumentNullException(nameof(action)),
            maxAttempts,
            delay ?? TimeSpan.Zero);

    /// <summary>无论主体成功、失败或取消，都尝试执行清理动作。</summary>
    public static LXAction Finally(LXAction body, LXAction cleanup, string name = "finally") =>
        new FinallyAction(
            name,
            body ?? throw new ArgumentNullException(nameof(body)),
            cleanup ?? throw new ArgumentNullException(nameof(cleanup)));

    private static IReadOnlyList<LXAction> ValidateActions(IReadOnlyList<LXAction>? actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0)
        {
            throw new ArgumentException("Composite actions require at least one child.", nameof(actions));
        }
        if (actions.Any(action => action is null))
        {
            throw new ArgumentException("Composite actions cannot contain null children.", nameof(actions));
        }
        return actions.ToArray();
    }

    private sealed class SequenceAction(string name, IReadOnlyList<LXAction> actions) : LXAction(name)
    {
        internal override async ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            foreach (var action in actions)
            {
                await context.RunChildAsync(action);
            }
        }
    }

    private sealed class ParallelAction(string name, IReadOnlyList<LXAction> actions) : LXAction(name)
    {
        internal override async ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var tasks = actions.Select(async action =>
            {
                try
                {
                    await context.RunChildAsync(action, operation.Token);
                }
                catch
                {
                    operation.Cancel();
                    throw;
                }
            }).ToArray();
            await Task.WhenAll(tasks);
        }
    }

    private sealed class RaceAction(string name, IReadOnlyList<LXAction> actions) : LXAction(name)
    {
        internal override async ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var tasks = actions
                .Select(action => context.RunChildAsync(action, operation.Token).AsTask())
                .ToArray();
            var winner = await Task.WhenAny(tasks);
            operation.Cancel();
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // The winner below defines Race's terminal result; losers are observed here.
            }
            await winner;
        }
    }

    private sealed class InvokeAction(string name, Action callback) : LXAction(name)
    {
        internal override ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            callback();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncAction(
        string name,
        Func<CancellationToken, ValueTask> callback) : LXAction(name)
    {
        internal override ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return callback(context.CancellationToken);
        }
    }

    private sealed class DelayAction : LXAction
    {
        private readonly TimeSpan _duration;

        public DelayAction(string name, TimeSpan duration)
            : base(name)
        {
            _duration = duration >= TimeSpan.Zero
                ? duration
                : throw new ArgumentOutOfRangeException(nameof(duration));
        }

        internal override async ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            await Task.Delay(_duration, context.CancellationToken);
        }
    }

    private sealed class TimeoutAction : LXAction
    {
        private readonly LXAction _action;
        private readonly TimeSpan _timeout;

        public TimeoutAction(string name, LXAction action, TimeSpan timeout)
            : base(name)
        {
            _action = action;
            _timeout = timeout > TimeSpan.Zero
                ? timeout
                : throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        internal override async ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                await context.RunChildAsync(_action, timeout.Token);
            }
            catch (OperationCanceledException exception) when (
                !context.CancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Action '{_action.Name}' exceeded {_timeout}.", exception);
            }
        }
    }

    private sealed class RetryAction : LXAction
    {
        private readonly LXAction _action;
        private readonly int _maxAttempts;
        private readonly TimeSpan _delay;

        public RetryAction(string name, LXAction action, int maxAttempts, TimeSpan delay)
            : base(name)
        {
            _action = action;
            _maxAttempts = maxAttempts > 0
                ? maxAttempts
                : throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            _delay = delay >= TimeSpan.Zero
                ? delay
                : throw new ArgumentOutOfRangeException(nameof(delay));
        }

        internal override async ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    await context.RunChildAsync(_action);
                    return;
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch when (attempt < _maxAttempts)
                {
                    if (_delay > TimeSpan.Zero)
                    {
                        await Task.Delay(_delay, context.CancellationToken);
                    }
                }
            }
        }
    }

    private sealed class FinallyAction(
        string name,
        LXAction body,
        LXAction cleanup) : LXAction(name)
    {
        internal override async ValueTask ExecuteAsync(ActionExecutionContext context)
        {
            try
            {
                await context.RunChildAsync(body);
            }
            finally
            {
                await context.RunCleanupAsync(cleanup);
            }
        }
    }
}
