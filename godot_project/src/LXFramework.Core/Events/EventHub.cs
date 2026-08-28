using LX.Core.Lifetime;

namespace LX.Core.Events;

public sealed class EventHub : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private bool _disposed;

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler, LifetimeScope? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = [];
                _handlers.Add(typeof(TEvent), handlers);
            }

            handlers.Add(handler);
        }

        var subscription = new DelegateDisposable(() => Unsubscribe(handler));
        return lifetime is null ? subscription : lifetime.Own(subscription);
    }

    public void Publish<TEvent>(TEvent message)
    {
        Delegate[] snapshot;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            snapshot = _handlers.TryGetValue(typeof(TEvent), out var handlers)
                ? [.. handlers]
                : [];
        }

        foreach (var handler in snapshot)
        {
            ((Action<TEvent>)handler)(message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _handlers.Clear();
        }
    }

    private void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        lock (_gate)
        {
            if (_disposed || !_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                return;
            }

            handlers.Remove(handler);
            if (handlers.Count == 0)
            {
                _handlers.Remove(typeof(TEvent));
            }
        }
    }
}
