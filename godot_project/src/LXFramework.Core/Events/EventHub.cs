using LX.Core.Lifetime;

namespace LX.Core.Events;

public sealed class EventHub : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly Dictionary<Type, Delegate[]> _snapshots = [];
    private readonly Action<Exception>? _handlerFailureSink;
    private readonly bool _isolateHandlerExceptions;
    private bool _disposed;

    /// <summary>
    /// 创建事件中心。启用异常隔离时，一个订阅者失败不会阻止其余订阅者，
    /// 异常会逐个报告给 <paramref name="handlerFailureSink"/>。
    /// </summary>
    public EventHub(
        Action<Exception>? handlerFailureSink = null,
        bool isolateHandlerExceptions = false)
    {
        _handlerFailureSink = handlerFailureSink;
        _isolateHandlerExceptions = isolateHandlerExceptions;
    }

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
            _snapshots[typeof(TEvent)] = handlers.ToArray();
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
                ? _snapshots[typeof(TEvent)]
                : Array.Empty<Delegate>();
        }

        foreach (var handler in snapshot)
        {
            if (!_isolateHandlerExceptions)
            {
                ((Action<TEvent>)handler)(message);
                continue;
            }

            try
            {
                ((Action<TEvent>)handler)(message);
            }
            catch (Exception exception)
            {
                try
                {
                    _handlerFailureSink?.Invoke(exception);
                }
                catch
                {
                    // The diagnostic path must not reintroduce dispatch failure.
                }
            }
        }
    }

    /// <summary>返回事件类型和订阅数量的不可变诊断快照。</summary>
    public EventHubSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new EventHubSnapshot(
                _handlers.Count,
                _handlers.Values.Sum(handlers => handlers.Count),
                _handlers
                    .OrderBy(pair => pair.Key.FullName, StringComparer.Ordinal)
                    .ToDictionary(
                        pair => pair.Key.FullName ?? pair.Key.Name,
                        pair => pair.Value.Count,
                        StringComparer.Ordinal));
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
            _snapshots.Clear();
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
                _snapshots.Remove(typeof(TEvent));
            }
            else
            {
                _snapshots[typeof(TEvent)] = handlers.ToArray();
            }
        }
    }
}

/// <summary>事件中心当前事件类型和订阅数量的诊断快照。</summary>
public sealed record EventHubSnapshot(
    int EventTypeCount,
    int SubscriptionCount,
    IReadOnlyDictionary<string, int> Subscriptions);
