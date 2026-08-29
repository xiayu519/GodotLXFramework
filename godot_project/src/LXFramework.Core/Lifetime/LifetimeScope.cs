namespace LX.Core.Lifetime;

public sealed class LifetimeScope : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private List<object>? _owned = [];
    private LifetimeScope? _parent;
    private int _disposed;

    public LifetimeScope(string name)
        : this(name, null)
    {
    }

    private LifetimeScope(string name, LifetimeScope? parent)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A lifetime scope must have a name.", nameof(name))
            : name.Trim();
        _parent = parent;
        _token = _cancellation.Token;
    }

    public string Name { get; }

    public CancellationToken Token => _token;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public int OwnedCount
    {
        get
        {
            lock (_gate)
            {
                return _owned?.Count ?? 0;
            }
        }
    }

    public LifetimeScope CreateChild(string name) => Own(new LifetimeScope($"{Name}/{name}", this));

    public T Own<T>(T owned) where T : notnull
    {
        if (owned is not IDisposable && owned is not IAsyncDisposable)
        {
            throw new ArgumentException("Owned values must implement IDisposable or IAsyncDisposable.", nameof(owned));
        }

        lock (_gate)
        {
            if (!IsDisposed)
            {
                _owned!.Add(owned);
                return owned;
            }
        }

        DisposeOwnedSynchronously(owned);
        throw new ObjectDisposedException(Name, "Cannot add ownership to a disposed lifetime scope.");
    }

    public IDisposable Defer(Action cleanup) => Own(new DelegateDisposable(cleanup));

    public void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public void Dispose()
    {
        var owned = BeginDispose(out var cancellationError);
        if (owned is null)
        {
            return;
        }

        List<Exception>? errors = null;
        if (cancellationError is not null)
        {
            (errors ??= []).Add(cancellationError);
        }

        for (var index = owned.Count - 1; index >= 0; index--)
        {
            try
            {
                DisposeOwnedSynchronously(owned[index]);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        _cancellation.Dispose();
        DetachFromParent();
        if (errors is not null)
        {
            throw new AggregateException($"Errors occurred while disposing lifetime scope '{Name}'.", errors);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var owned = BeginDispose(out var cancellationError);
        if (owned is null)
        {
            return;
        }

        List<Exception>? errors = null;
        if (cancellationError is not null)
        {
            (errors ??= []).Add(cancellationError);
        }

        for (var index = owned.Count - 1; index >= 0; index--)
        {
            try
            {
                if (owned[index] is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    ((IDisposable)owned[index]).Dispose();
                }
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        _cancellation.Dispose();
        DetachFromParent();
        if (errors is not null)
        {
            throw new AggregateException($"Errors occurred while disposing lifetime scope '{Name}'.", errors);
        }
    }

    /// <summary>
    /// 取消生命周期并启动逆序清理，但不阻塞等待尚未完成的异步所有者。
    /// 仅用于主线程已经不能安全等待异步 continuation 的紧急退出路径；
    /// 常规关闭仍应使用 <see cref="DisposeAsync"/>。
    /// </summary>
    public void DisposeEmergency(Action<Exception>? failureSink = null)
    {
        var owned = BeginDispose(out var cancellationError);
        if (owned is null)
        {
            return;
        }

        ReportEmergencyFailure(cancellationError, failureSink);
        List<Task>? pending = null;
        for (var index = owned.Count - 1; index >= 0; index--)
        {
            try
            {
                switch (owned[index])
                {
                    case LifetimeScope child:
                        child.DisposeEmergency(failureSink);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                    case IAsyncDisposable asyncDisposable:
                    {
                        var disposal = asyncDisposable.DisposeAsync();
                        if (disposal.IsCompletedSuccessfully)
                        {
                            disposal.GetAwaiter().GetResult();
                        }
                        else
                        {
                            (pending ??= []).Add(disposal.AsTask());
                        }
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                ReportEmergencyFailure(exception, failureSink);
            }
        }

        if (pending is null)
        {
            CompleteDispose();
            return;
        }

        _ = CompleteEmergencyAsync(pending, failureSink);
    }

    private List<object>? BeginDispose(out Exception? cancellationError)
    {
        cancellationError = null;
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return null;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (Exception exception)
        {
            cancellationError = exception;
        }

        lock (_gate)
        {
            var owned = _owned!;
            _owned = null;
            return owned;
        }
    }

    private static void DisposeOwnedSynchronously(object owned)
    {
        if (owned is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        ((IAsyncDisposable)owned).DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async Task CompleteEmergencyAsync(
        IReadOnlyList<Task> pending,
        Action<Exception>? failureSink)
    {
        foreach (var task in pending)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ReportEmergencyFailure(exception, failureSink);
            }
        }

        CompleteDispose();
    }

    private void CompleteDispose()
    {
        _cancellation.Dispose();
        DetachFromParent();
    }

    private static void ReportEmergencyFailure(
        Exception? exception,
        Action<Exception>? failureSink)
    {
        if (exception is null || failureSink is null)
        {
            return;
        }

        try
        {
            failureSink(exception);
        }
        catch
        {
            // Emergency cleanup cannot allow its diagnostic path to fail teardown.
        }
    }

    private void DetachFromParent()
    {
        var parent = Interlocked.Exchange(ref _parent, null);
        parent?.ReleaseOwned(this);
    }

    private void ReleaseOwned(object owned)
    {
        lock (_gate)
        {
            _owned?.Remove(owned);
        }
    }
}
