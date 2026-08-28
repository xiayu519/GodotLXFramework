using LX.Res;
using LX.Core.Diagnostics;
using LX.Core.Events;
using LX.Core.Lifetime;
using LX.Runtime;
using Godot;

namespace LX.Scenes;

public sealed class SceneService : IAsyncDisposable
{
    private sealed class ActiveScene
    {
        public required string Path { get; init; }
        public required Node Node { get; init; }
        public required LifetimeScope Lifetime { get; init; }
        public required AssetLease<PackedScene> Lease { get; init; }
    }

    private readonly Node _host;
    private readonly Node _worldRoot;
    private readonly AssetRegistry _assets;
    private readonly LifetimeScope _rootLifetime;
    private readonly EventHub _events;
    private readonly MetricRegistry _metrics;
    private readonly Func<LXContext> _context;
    private readonly Dictionary<WorldId, WorldDescriptor> _catalog = [];
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly int _mainThreadId;
    private ActiveScene? _active;
    private bool _disposed;

    public SceneService(
        Node host,
        AssetRegistry assets,
        LifetimeScope rootLifetime,
        EventHub events,
        MetricRegistry metrics,
        Func<LXContext> context)
    {
        _host = host;
        _assets = assets;
        _rootLifetime = rootLifetime;
        _events = events;
        _metrics = metrics;
        _context = context;
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        _worldRoot = new Node
        {
            Name = "World",
            ProcessMode = Node.ProcessModeEnum.Pausable,
        };
        _host.AddChild(_worldRoot);
        UpdateMetrics();
    }

    public string? ActivePath
    {
        get
        {
            EnsureMainThread();
            return _active?.Path;
        }
    }

    public Node? ActiveNode
    {
        get
        {
            EnsureMainThread();
            return _active?.Node;
        }
    }

    public void Register(WorldDescriptor descriptor)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (!_catalog.TryAdd(descriptor.Id, descriptor))
        {
            throw new InvalidOperationException($"World ID '{descriptor.Id}' is already registered.");
        }
    }

    public void RegisterRange(IEnumerable<WorldDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        foreach (var descriptor in descriptors)
        {
            Register(descriptor);
        }
    }

    public ValueTask ChangeAsync(WorldId worldId, CancellationToken cancellationToken = default) =>
        ChangeAsync(worldId, SceneTransitionMode.ReleasePreviousBeforeLoad, cancellationToken);

    /// <summary>后台预载一个已注册世界，并持续报告实际资源加载进度。</summary>
    public ValueTask<ScenePreload> PreloadAsync(
        WorldId worldId,
        Action<SceneLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        if (!_catalog.TryGetValue(worldId, out var descriptor))
        {
            throw new KeyNotFoundException($"World '{worldId}' is not registered.");
        }
        return PreloadAsync(descriptor.ScenePath, progress, cancellationToken);
    }

    /// <summary>后台预载 res:// 场景并返回保持资源存活的租约。</summary>
    public async ValueTask<ScenePreload> PreloadAsync(
        string scenePath,
        Action<SceneLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ValidateScenePath(scenePath);
        progress?.Invoke(new SceneLoadProgress(scenePath, SceneLoadStage.LoadingResource, 0));
        var lease = await _assets.AcquireAsync<PackedScene>(
            scenePath,
            AssetCachePolicy.Cached,
            ratio => progress?.Invoke(new SceneLoadProgress(
                scenePath,
                SceneLoadStage.LoadingResource,
                ratio * 0.95f)),
            cancellationToken);
        progress?.Invoke(new SceneLoadProgress(scenePath, SceneLoadStage.Ready, 1));
        return new ScenePreload(scenePath, lease);
    }

    public ValueTask ChangeAsync(
        WorldId worldId,
        SceneTransitionMode mode,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        if (!_catalog.TryGetValue(worldId, out var descriptor))
        {
            throw new KeyNotFoundException($"World '{worldId}' is not registered.");
        }

        return ChangeAsync(descriptor.ScenePath, mode, cancellationToken);
    }

    public ValueTask ChangeAsync(string scenePath, CancellationToken cancellationToken = default) =>
        ChangeAsync(scenePath, SceneTransitionMode.ReleasePreviousBeforeLoad, cancellationToken);

    public async ValueTask ChangeAsync(
        string scenePath,
        SceneTransitionMode mode,
        CancellationToken cancellationToken = default)
        => await ChangeAsync(scenePath, mode, progress: null, cancellationToken);

    /// <summary>切换世界场景，并报告资源读取和实例化阶段的进度。</summary>
    public async ValueTask ChangeAsync(
        string scenePath,
        SceneTransitionMode mode,
        Action<SceneLoadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ValidateScenePath(scenePath);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_active?.Path, scenePath, StringComparison.Ordinal))
            {
                return;
            }

            if (!ResourceLoader.Exists(scenePath, "PackedScene"))
            {
                throw new FileNotFoundException($"World scene '{scenePath}' does not exist.", scenePath);
            }

            var previousPath = _active?.Path;
            _events.Publish(new WorldSceneChangeStarted(previousPath, scenePath, mode));
            ActiveScene? next = null;
            try
            {
                if (mode == SceneTransitionMode.ReleasePreviousBeforeLoad)
                {
                    await ReleaseActiveAsync();
                    _assets.PurgeIdleCache();
                }

                next = await PrepareAsync(scenePath, progress, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (mode == SceneTransitionMode.KeepPreviousUntilReady)
                {
                    await ReleaseActiveAsync();
                    _assets.PurgeIdleCache();
                }

                _worldRoot.AddChild(next.Node);
                _active = next;
                next = null;
                UpdateMetrics();
                _events.Publish(new WorldSceneChanged(previousPath, scenePath));
            }
            catch (Exception exception)
            {
                if (next is not null)
                {
                    await DisposePreparedAsync(next);
                }
                _events.Publish(new WorldSceneChangeFailed(
                    previousPath,
                    scenePath,
                    mode,
                    exception.Message));
                throw;
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        EnsureMainThread();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _transitionGate.WaitAsync();
        try
        {
            if (_active is not null)
            {
                _active.Node.QueueFree();
                await _active.Lifetime.DisposeAsync();
                _active.Lease.Dispose();
                _active = null;
            }

            _worldRoot.QueueFree();
            _catalog.Clear();
            UpdateMetrics();
        }
        finally
        {
            _transitionGate.Release();
            _transitionGate.Dispose();
        }
    }

    private async ValueTask<ActiveScene> PrepareAsync(
        string scenePath,
        Action<SceneLoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Invoke(new SceneLoadProgress(scenePath, SceneLoadStage.LoadingResource, 0));
        var lease = await _assets.AcquireAsync<PackedScene>(
            scenePath,
            AssetCachePolicy.Transient,
            ratio => progress?.Invoke(new SceneLoadProgress(
                scenePath,
                SceneLoadStage.LoadingResource,
                ratio * 0.9f)),
            cancellationToken);
        var lifetime = _rootLifetime.CreateChild($"Scene:{scenePath}");
        Node? node = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(new SceneLoadProgress(scenePath, SceneLoadStage.Instantiating, 0.95f));
            node = lease.Resource.Instantiate();
            LXContextInjector.InitializeTree(node, _context(), lifetime);
            progress?.Invoke(new SceneLoadProgress(scenePath, SceneLoadStage.Ready, 1));
            return new ActiveScene
            {
                Path = scenePath,
                Node = node,
                Lifetime = lifetime,
                Lease = lease,
            };
        }
        catch
        {
            node?.QueueFree();
            await lifetime.DisposeAsync();
            lease.Dispose();
            throw;
        }
    }

    private async ValueTask ReleaseActiveAsync()
    {
        if (_active is null)
        {
            return;
        }

        var previous = _active;
        _active = null;
        previous.Node.QueueFree();
        await previous.Lifetime.DisposeAsync();
        previous.Lease.Dispose();
        UpdateMetrics();

        // Let queued nodes leave the tree before loading the next potentially
        // large map. This intentionally favors bounded peak memory over keeping
        // the previous scene visible if the next load is cancelled.
        await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static async ValueTask DisposePreparedAsync(ActiveScene scene)
    {
        scene.Node.QueueFree();
        await scene.Lifetime.DisposeAsync();
        scene.Lease.Dispose();
    }

    private void UpdateMetrics() => _metrics.SetGauge("scene.active", _active is null ? 0 : 1);

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Scene transitions must run on Godot's main thread.");
        }
    }

    private static void ValidateScenePath(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath) ||
            !scenePath.StartsWith("res://", StringComparison.Ordinal) ||
            !scenePath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Scene paths must be non-empty res:// .tscn paths.", nameof(scenePath));
        }
    }
}

public readonly record struct WorldSceneChanged(string? PreviousPath, string CurrentPath);

public readonly record struct WorldSceneChangeStarted(
    string? PreviousPath,
    string CurrentPath,
    SceneTransitionMode Mode);

public readonly record struct WorldSceneChangeFailed(
    string? PreviousPath,
    string CurrentPath,
    SceneTransitionMode Mode,
    string Error);

/// <summary>世界切换期间旧场景与新场景的资源存活顺序。</summary>
public enum SceneTransitionMode
{
    /// <summary>Minimizes peak memory; failure may leave no active world.</summary>
    ReleasePreviousBeforeLoad,

    /// <summary>Keeps the old world alive until the replacement is ready.</summary>
    KeepPreviousUntilReady,
}
