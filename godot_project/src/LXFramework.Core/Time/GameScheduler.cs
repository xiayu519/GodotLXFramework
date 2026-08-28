using LX.Core.Lifetime;

namespace LX.Core.Time;

public sealed class GameScheduler : IDisposable
{
    private sealed class ScheduledItem
    {
        public required long Id { get; init; }
        public required double DueAt { get; init; }
        public required Action Callback { get; init; }
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
        while (_queue.TryPeek(out var item, out var priority) && priority.DueAt <= _clock.ElapsedSeconds)
        {
            _queue.Dequeue();
            _items.Remove(item.Id);
            if (!item.Cancelled)
            {
                item.Callback();
            }
        }
    }

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
        }
    }
}
