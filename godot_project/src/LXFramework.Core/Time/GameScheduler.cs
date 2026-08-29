using LX.Core.Lifetime;

namespace LX.Core.Time;

public sealed class GameScheduler : IDisposable
{
    private sealed class ScheduledItem
    {
        public required long Id { get; init; }
        public required double DueAt { get; init; }
        public Action? Callback { get; set; }
        public bool Cancelled { get; set; }
    }

    private readonly GameClock _clock;
    private readonly PriorityQueue<ScheduledItem, (double DueAt, long Id)> _queue = new();
    private readonly Dictionary<long, ScheduledItem> _items = [];
    private long _nextId;
    private bool _disposed;

    public GameScheduler(GameClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public int PendingCount => _items.Count;

    public IDisposable Schedule(TimeSpan delay, Action callback, LifetimeScope? lifetime = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        var item = new ScheduledItem
        {
            Id = ++_nextId,
            DueAt = _clock.ElapsedSeconds + delay.TotalSeconds,
            Callback = callback,
        };
        _items.Add(item.Id, item);
        _queue.Enqueue(item, (item.DueAt, item.Id));

        var handle = new DelegateDisposable(() => Cancel(item.Id));
        return lifetime is null ? handle : lifetime.Own(handle);
    }

    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lastScheduledBeforeTick = _nextId;
        while (_queue.TryPeek(out var item, out var priority) && priority.DueAt <= _clock.ElapsedSeconds)
        {
            if (item.Id > lastScheduledBeforeTick)
            {
                break;
            }

            _queue.Dequeue();
            _items.Remove(item.Id);
            if (!item.Cancelled && item.Callback is { } callback)
            {
                callback();
            }
        }
    }

    /// <summary>返回当前调度队列和活动任务数量的诊断快照。</summary>
    public GameSchedulerSnapshot Snapshot() => new(
        _items.Count,
        _queue.Count,
        _clock.ElapsedSeconds,
        _disposed);

    public void Dispose()
    {
        _disposed = true;
        _items.Clear();
        _queue.Clear();
    }

    private void Cancel(long id)
    {
        if (_items.Remove(id, out var item))
        {
            item.Cancelled = true;
            item.Callback = null;
            if (_queue.Count > 64 && _queue.Count > _items.Count * 2)
            {
                _queue.Clear();
                foreach (var pending in _items.Values)
                {
                    _queue.Enqueue(pending, (pending.DueAt, pending.Id));
                }
            }
        }
    }
}

/// <summary>游戏调度器的不可变诊断快照。</summary>
public sealed record GameSchedulerSnapshot(
    int PendingCount,
    int QueueCount,
    double ElapsedSeconds,
    bool IsDisposed);
