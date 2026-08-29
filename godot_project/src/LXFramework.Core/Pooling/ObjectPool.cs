namespace LX.Core.Pooling;

public sealed class ObjectPool<T> : IDisposable where T : class
{
    private readonly object _gate = new();
    private readonly Func<T> _factory;
    private readonly Action<T>? _reset;
    private readonly Action<T>? _discard;
    private readonly Stack<T> _available = [];
    private readonly HashSet<T> _ownedSet = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<T> _rentedSet = new(ReferenceEqualityComparer.Instance);
    private long _createdCount;
    private long _reusedCount;
    private long _discardedCount;
    private bool _disposed;

    public ObjectPool(
        Func<T> factory,
        Action<T>? reset = null,
        Action<T>? discard = null,
        int maxRetained = 128)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _reset = reset;
        _discard = discard;
        MaxRetained = maxRetained > 0
            ? maxRetained
            : throw new ArgumentOutOfRangeException(nameof(maxRetained));
    }

    public int MaxRetained { get; }

    public int RetainedCount
    {
        get
        {
            lock (_gate)
            {
                return _available.Count;
            }
        }
    }

    public int RentedCount
    {
        get
        {
            lock (_gate)
            {
                return _rentedSet.Count;
            }
        }
    }

    public PoolStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new PoolStatistics(
                    _createdCount,
                    _reusedCount,
                    _discardedCount,
                    _rentedSet.Count,
                    _available.Count);
            }
        }
    }

    public T Rent()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_available.TryPop(out var item))
            {
                _rentedSet.Add(item);
                _reusedCount++;
                return item;
            }
        }

        var created = _factory() ??
            throw new InvalidOperationException("Object pool factory returned null.");
        lock (_gate)
        {
            _createdCount++;
            if (!_disposed)
            {
                _ownedSet.Add(created);
                _rentedSet.Add(created);
                return created;
            }
        }

        lock (_gate)
        {
            _discardedCount++;
        }
        _discard?.Invoke(created);
        throw new ObjectDisposedException(nameof(ObjectPool<T>));
    }

    public PooledObject<T> RentLease() => new(this, Rent());

    public void Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var retain = false;

        lock (_gate)
        {
            if (!_ownedSet.Contains(item))
            {
                throw new InvalidOperationException("The object does not belong to this pool.");
            }
            if (!_rentedSet.Remove(item))
            {
                throw new InvalidOperationException("The same object was returned to the pool more than once.");
            }

            if (_disposed || _available.Count >= MaxRetained)
            {
                _ownedSet.Remove(item);
                _discardedCount++;
            }
            else
            {
                retain = true;
            }
        }

        if (!retain)
        {
            _discard?.Invoke(item);
            return;
        }

        try
        {
            _reset?.Invoke(item);
        }
        catch (Exception resetException)
        {
            lock (_gate)
            {
                _ownedSet.Remove(item);
                _discardedCount++;
            }
            try
            {
                _discard?.Invoke(item);
            }
            catch (Exception discardException)
            {
                throw new AggregateException(
                    "Pool reset and discard both failed.",
                    resetException,
                    discardException);
            }
            throw;
        }

        var discardAfterReset = false;
        lock (_gate)
        {
            if (_disposed || _available.Count >= MaxRetained)
            {
                _ownedSet.Remove(item);
                _discardedCount++;
                discardAfterReset = true;
            }
            else
            {
                _available.Push(item);
            }
        }

        if (discardAfterReset)
        {
            _discard?.Invoke(item);
        }
    }

    public void Dispose()
    {
        T[] discarded;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            discarded = _available.ToArray();
            foreach (var item in discarded)
            {
                _ownedSet.Remove(item);
            }
            _discardedCount += discarded.Length;
            _available.Clear();
        }

        List<Exception>? errors = null;
        foreach (var item in discarded)
        {
            try
            {
                _discard?.Invoke(item);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more pooled objects could not be discarded.", errors);
        }
    }
}

public readonly record struct PoolStatistics(
    long Created,
    long Reused,
    long Discarded,
    int Rented,
    int Retained);

public sealed class PooledObject<T> : IDisposable where T : class
{
    private ObjectPool<T>? _pool;

    internal PooledObject(ObjectPool<T> pool, T value)
    {
        _pool = pool;
        Value = value;
    }

    public T Value { get; }

    public void Dispose() => Interlocked.Exchange(ref _pool, null)?.Return(Value);
}
