using LX.Core.Diagnostics;
using Godot;

namespace LX.Res;

public sealed class AssetRegistry : IDisposable
{
    private sealed class Entry
    {
        public required Resource Resource { get; init; }
        public required AssetCachePolicy Policy { get; set; }
        public int LeaseCount { get; set; }
        public long LastTouched { get; set; }
    }

    private readonly object _gate = new();
    private readonly Node _host;
    private readonly MetricRegistry _metrics;
    private readonly int _mainThreadId;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<Resource>> _inflight = new(StringComparer.Ordinal);
    private int _maxIdleCacheEntries;
    private long _touchSequence;
    private bool _disposed;

    public AssetRegistry(Node host, MetricRegistry metrics, int maxIdleCacheEntries = 32)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        MaxIdleCacheEntries = maxIdleCacheEntries >= 0
            ? maxIdleCacheEntries
            : throw new ArgumentOutOfRangeException(nameof(maxIdleCacheEntries));
    }

    public int MaxIdleCacheEntries
    {
        get
        {
            lock (_gate)
            {
                return _maxIdleCacheEntries;
            }
        }
        set
        {
            EnsureMainThread();
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _maxIdleCacheEntries = value;
                TrimIdleCacheLocked();
                UpdateMetricsLocked();
            }
        }
    }

    public AssetLease<T> Acquire<T>(string path, AssetCachePolicy policy = AssetCachePolicy.Transient)
        where T : Resource
    {
        EnsureMainThread();
        ValidatePath(path);
        ValidatePolicy(policy);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_inflight.ContainsKey(path))
            {
                throw new InvalidOperationException(
                    $"Asset '{path}' is currently loading asynchronously; await AcquireAsync instead of blocking the main thread.");
            }

            if (_entries.TryGetValue(path, out var existing))
            {
                return AcquireExisting<T>(path, existing, policy);
            }
        }

        var resource = GD.Load<T>(path) ??
            throw new InvalidOperationException($"Godot failed to load asset '{path}' as {typeof(T).Name}.");
        return AcquireLoaded(path, resource, policy);
    }

    public AssetLease<T> Acquire<T>(AssetRef<T> asset) where T : Resource =>
        Acquire<T>(asset.Path, asset.CachePolicy);

    public AssetLease<T> AcquireGenerated<T>(
        string key,
        Func<T> factory,
        AssetCachePolicy policy = AssetCachePolicy.Cached)
        where T : Resource
    {
        EnsureMainThread();
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("generated://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Generated asset keys must use the generated:// scheme.", nameof(key));
        }
        ArgumentNullException.ThrowIfNull(factory);
        ValidatePolicy(policy);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out var existing))
            {
                return AcquireExisting<T>(key, existing, policy);
            }
        }

        var resource = factory() ?? throw new InvalidOperationException(
            $"Generated asset factory '{key}' returned null.");
        return AcquireLoaded(key, resource, policy);
    }

    public async ValueTask<AssetLease<T>> AcquireAsync<T>(
        string path,
        AssetCachePolicy policy = AssetCachePolicy.Transient,
        CancellationToken cancellationToken = default)
        where T : Resource =>
        await AcquireAsync<T>(path, policy, progress: null, cancellationToken);

    /// <summary>异步取得资源，并在 Godot 后台加载期间报告 0 到 1 的实际进度。</summary>
    public async ValueTask<AssetLease<T>> AcquireAsync<T>(
        string path,
        AssetCachePolicy policy,
        Action<float>? progress,
        CancellationToken cancellationToken = default)
        where T : Resource
    {
        EnsureMainThread();
        ValidatePath(path);
        ValidatePolicy(policy);
        cancellationToken.ThrowIfCancellationRequested();

        Task<Resource> loadTask;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(path, out var existing))
            {
                progress?.Invoke(1);
                return AcquireExisting<T>(path, existing, policy);
            }

            if (!_inflight.TryGetValue(path, out loadTask!))
            {
                loadTask = LoadThreadedAsync(path, policy, progress);
                _inflight.Add(path, loadTask);
                _ = ObserveInflightAsync(path, loadTask);
            }
        }

        var loaded = await loadTask.WaitAsync(cancellationToken);
        EnsureMainThread();
        cancellationToken.ThrowIfCancellationRequested();
        if (loaded is not T typed)
        {
            throw new InvalidCastException(
                $"Asset '{path}' loaded as {loaded.GetType().Name}, not {typeof(T).Name}.");
        }

        progress?.Invoke(1);
        return AcquireLoaded(path, typed, policy);
    }

    public ValueTask<AssetLease<T>> AcquireAsync<T>(
        AssetRef<T> asset,
        CancellationToken cancellationToken = default)
        where T : Resource =>
        AcquireAsync<T>(asset.Path, asset.CachePolicy, cancellationToken);

    /// <summary>加载并持有一个命名预热集合；释放返回值会按反向顺序归还全部资源租约。</summary>
    public ValueTask<AssetBatchLease<T>> PreloadAsync<T>(
        AssetPreloadSet<T> set,
        int maxConcurrency = 4,
        Action<AssetLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
        where T : Resource
    {
        ArgumentNullException.ThrowIfNull(set);
        var report = set.Analyze();
        if (!report.IsValid)
        {
            throw new InvalidOperationException(
                $"Asset preload set '{set.Id}' is invalid: {report.Status}; " +
                $"missing=[{string.Join(", ", report.MissingDependencies)}]; " +
                $"cycles=[{string.Join(", ", report.CyclicAssets)}].");
        }
        return AcquireBatchAsync(set.Requests, maxConcurrency, progress, cancellationToken);
    }

    public async ValueTask<AssetBatchLease<T>> AcquireBatchAsync<T>(
        IEnumerable<AssetLoadRequest<T>> requests,
        int maxConcurrency = 4,
        Action<AssetLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
        where T : Resource
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requests);
        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        var requestArray = requests.ToArray();
        var report = AssetDependencyAnalyzer.Analyze(requestArray);
        if (!report.IsValid)
        {
            throw new ArgumentException(
                $"Asset batch is invalid: {report.Status}; " +
                $"missing=[{string.Join(", ", report.MissingDependencies)}]; " +
                $"cycles=[{string.Join(", ", report.CyclicAssets)}].",
                nameof(requests));
        }
        var pending = requestArray.ToDictionary(request => request.Id, StringComparer.Ordinal);

        var total = pending.Count;
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var active = new Dictionary<string, Task<AssetLease<T>>>(StringComparer.Ordinal);
        var leases = new Dictionary<string, AssetLease<T>>(StringComparer.Ordinal);
        var acquisitionOrder = new List<string>(total);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        progress?.Invoke(new AssetLoadProgress(0, total, null));

        try
        {
            while (pending.Count > 0 || active.Count > 0)
            {
                while (active.Count < maxConcurrency)
                {
                    var next = pending.Values
                        .Where(request => (request.Dependencies ?? []).All(completed.Contains))
                        .OrderByDescending(request => request.Priority)
                        .ThenBy(request => request.Id, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (next is null)
                    {
                        break;
                    }

                    pending.Remove(next.Id);
                    active.Add(next.Id, AcquireAsync(next.Asset, operation.Token).AsTask());
                }

                if (active.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Asset batch dependencies contain a cycle and cannot be scheduled.");
                }

                var finishedTask = await Task.WhenAny(active.Values);
                EnsureMainThread();
                var finished = active.Single(pair => ReferenceEquals(pair.Value, finishedTask));
                active.Remove(finished.Key);
                var lease = await finished.Value;
                EnsureMainThread();
                leases.Add(finished.Key, lease);
                acquisitionOrder.Add(finished.Key);
                completed.Add(finished.Key);
                progress?.Invoke(new AssetLoadProgress(completed.Count, total, finished.Key));
            }

            return new AssetBatchLease<T>(leases, acquisitionOrder);
        }
        catch
        {
            operation.Cancel();
            foreach (var task in active.Values)
            {
                try
                {
                    var lease = await task;
                    EnsureMainThread();
                    lease.Dispose();
                }
                catch (Exception) when (task.IsCanceled || task.IsFaulted)
                {
                }
            }
            for (var index = acquisitionOrder.Count - 1; index >= 0; index--)
            {
                leases[acquisitionOrder[index]].Dispose();
            }
            throw;
        }
    }

    public IReadOnlyList<AssetRecord> Snapshot()
    {
        EnsureMainThread();
        lock (_gate)
        {
            return _entries
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new AssetRecord(
                    pair.Key,
                    pair.Value.Resource.GetType().Name,
                    pair.Value.LeaseCount,
                    pair.Value.Policy))
                .ToArray();
        }
    }

    public void TrimIdleCache()
    {
        EnsureMainThread();
        lock (_gate)
        {
            TrimIdleCacheLocked();
            UpdateMetricsLocked();
        }
    }

    public void PurgeIdleCache()
    {
        EnsureMainThread();
        lock (_gate)
        {
            foreach (var path in _entries
                         .Where(pair => pair.Value.Policy == AssetCachePolicy.Cached && pair.Value.LeaseCount == 0)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _entries[path].Resource.Dispose();
                _entries.Remove(path);
            }

            UpdateMetricsLocked();
        }
    }

    public void Dispose()
    {
        EnsureMainThread();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                entry.Resource.Dispose();
            }
            _entries.Clear();
            _inflight.Clear();
            UpdateMetricsLocked();
        }
    }

    internal void Release(string path, Resource resource)
    {
        EnsureMainThread();
        lock (_gate)
        {
            if (_disposed || !_entries.TryGetValue(path, out var entry) || !ReferenceEquals(entry.Resource, resource))
            {
                return;
            }

            if (entry.LeaseCount <= 0)
            {
                throw new InvalidOperationException($"Asset '{path}' was released more times than it was acquired.");
            }

            entry.LeaseCount--;
            entry.LastTouched = ++_touchSequence;
            if (entry.LeaseCount == 0 && entry.Policy == AssetCachePolicy.Transient)
            {
                entry.Resource.Dispose();
                _entries.Remove(path);
            }

            TrimIdleCacheLocked();
            UpdateMetricsLocked();
        }
    }

    private AssetLease<T> AcquireLoaded<T>(string path, T resource, AssetCachePolicy policy)
        where T : Resource
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(path, out var existing))
            {
                return AcquireExisting<T>(path, existing, policy);
            }

            var entry = new Entry
            {
                Resource = resource,
                Policy = policy,
                LeaseCount = 1,
                LastTouched = ++_touchSequence,
            };
            _entries.Add(path, entry);
            UpdateMetricsLocked();
            return new AssetLease<T>(this, path, resource);
        }
    }

    private AssetLease<T> AcquireExisting<T>(string path, Entry entry, AssetCachePolicy requestedPolicy)
        where T : Resource
    {
        if (entry.Resource is not T typed)
        {
            throw new InvalidCastException(
                $"Asset '{path}' is already registered as {entry.Resource.GetType().Name}, not {typeof(T).Name}.");
        }

        entry.Policy = (AssetCachePolicy)Math.Max((int)entry.Policy, (int)requestedPolicy);
        entry.LeaseCount++;
        entry.LastTouched = ++_touchSequence;
        UpdateMetricsLocked();
        return new AssetLease<T>(this, path, typed);
    }

    private async Task<Resource> LoadThreadedAsync(
        string path,
        AssetCachePolicy policy,
        Action<float>? progress)
    {
        var cacheMode = policy == AssetCachePolicy.Transient
            ? ResourceLoader.CacheMode.Ignore
            : ResourceLoader.CacheMode.Reuse;
        var error = ResourceLoader.LoadThreadedRequest(path, cacheMode: cacheMode);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Threaded load request for '{path}' failed with {error}.");
        }

        while (true)
        {
            var values = new Godot.Collections.Array();
            var status = ResourceLoader.LoadThreadedGetStatus(path, values);
            if (values.Count > 0)
            {
                progress?.Invoke(Math.Clamp(values[0].AsSingle(), 0, 1));
            }
            switch (status)
            {
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    return ResourceLoader.LoadThreadedGet(path) ??
                           throw new InvalidOperationException($"Threaded load returned null for '{path}'.");
                case ResourceLoader.ThreadLoadStatus.Failed:
                case ResourceLoader.ThreadLoadStatus.InvalidResource:
                    throw new InvalidOperationException($"Threaded load for '{path}' ended with {status}.");
                case ResourceLoader.ThreadLoadStatus.InProgress:
                    await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }
    }

    private async Task ObserveInflightAsync(string path, Task<Resource> loadTask)
    {
        try
        {
            await loadTask;
        }
        catch
        {
            _metrics.Increment("assets.load_failures");
        }
        finally
        {
            lock (_gate)
            {
                if (_inflight.TryGetValue(path, out var current) && ReferenceEquals(current, loadTask))
                {
                    _inflight.Remove(path);
                }

                UpdateMetricsLocked();
            }
        }
    }

    private void TrimIdleCacheLocked()
    {
        var idleCached = _entries
            .Where(pair => pair.Value.Policy == AssetCachePolicy.Cached && pair.Value.LeaseCount == 0)
            .OrderBy(pair => pair.Value.LastTouched)
            .ToArray();
        var removeCount = Math.Max(0, idleCached.Length - _maxIdleCacheEntries);
        for (var index = 0; index < removeCount; index++)
        {
            idleCached[index].Value.Resource.Dispose();
            _entries.Remove(idleCached[index].Key);
        }
    }

    private void UpdateMetricsLocked()
    {
        _metrics.SetGauge("assets.entries", _entries.Count);
        _metrics.SetGauge("assets.inflight", _inflight.Count);
        _metrics.SetGauge("assets.leases", _entries.Values.Sum(entry => entry.LeaseCount));
    }

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Godot assets must be acquired from the main thread.");
        }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Asset paths must be non-empty res:// paths.", nameof(path));
        }
    }

    private static void ValidatePolicy(AssetCachePolicy policy)
    {
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }
}

public sealed record AssetRecord(
    string Path,
    string ResourceType,
    int LeaseCount,
    AssetCachePolicy Policy);
