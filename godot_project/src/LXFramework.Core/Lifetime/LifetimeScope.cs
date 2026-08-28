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
