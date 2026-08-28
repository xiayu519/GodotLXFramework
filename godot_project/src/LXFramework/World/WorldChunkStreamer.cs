using LX.Res;
using LX.Core.Diagnostics;
using LX.Core.Lifetime;
using LX.Core.World;
using LX.Runtime;
using Godot;

namespace LX.World;

public sealed class WorldChunkStreamer : IAsyncDisposable
{
    private sealed record ActiveChunk(Node Node, LifetimeScope Lifetime);

    private readonly Node _parent;
    private readonly IWorldChunkSource _source;
    private readonly MetricRegistry _metrics;
    private readonly Func<LXContext> _context;
    private readonly LifetimeScope _lifetime;
    private readonly IReadOnlySet<ChunkCoordinate> _available;
    private readonly int _chunkWidth;
    private readonly int _chunkHeight;
    private readonly Dictionary<ChunkCoordinate, ActiveChunk> _active = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _mainThreadId;
    private CancellationTokenSource? _focusChange;
    private ChunkCoordinate? _lastFocus;
    private int _lastRadius = -1;
    private bool _disposed;

    public WorldChunkStreamer(
        Node parent,
        AssetRegistry assets,
        LifetimeScope parentLifetime,
        MetricRegistry metrics,
        Func<LXContext> context,
        WorldChunkManifest manifest)
        : this(
            parent,
            new PackedSceneWorldChunkSource(assets, manifest),
            parentLifetime,
            metrics,
            context)
    {
    }

    public WorldChunkStreamer(
        Node parent,
        IWorldChunkSource source,
        LifetimeScope parentLifetime,
        MetricRegistry metrics,
        Func<LXContext> context)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _available = source.Coordinates.ToHashSet();
        if (_available.Count == 0 || _available.Count != source.Coordinates.Count)
        {
            throw new InvalidDataException("World chunk source coordinates must be non-empty and unique.");
        }
        if (source.ChunkWidth <= 0 || source.ChunkHeight <= 0)
        {
            throw new InvalidDataException("World chunk source dimensions must be positive.");
        }
        _chunkWidth = source.ChunkWidth;
        _chunkHeight = source.ChunkHeight;
        _lifetime = parentLifetime.CreateChild("WorldChunkStreamer");
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        UpdateMetrics();
    }

    public IReadOnlyCollection<ChunkCoordinate> ActiveChunks
    {
        get
        {
            EnsureMainThread();
            return _active.Keys.ToArray();
        }
    }

    public ChunkCoordinate WorldToChunk(Vector2 worldPosition)
    {
        EnsureMainThread();
        return new ChunkCoordinate(
            Mathf.FloorToInt(worldPosition.X / _chunkWidth),
            Mathf.FloorToInt(worldPosition.Y / _chunkHeight));
    }

    public ValueTask SetFocusAsync(
        ChunkCoordinate focus,
        int radius = 1,
        CancellationToken cancellationToken = default) =>
        SetFocusAsync(
            focus,
            new WorldChunkStreamingOptions { Radius = radius },
            cancellationToken);

    public async ValueTask SetFocusAsync(
        ChunkCoordinate focus,
        WorldChunkStreamingOptions options,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (_focusChange is null && _lastFocus == focus && _lastRadius == options.Radius)
        {
            return;
        }

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        _focusChange?.Cancel();
        _focusChange = operation;
        await _gate.WaitAsync(operation.Token);
        try
        {
            var targets = ChunkPlanner.VisibleSquare(focus, options.Radius, _source.IsAvailable);
            var targetSet = targets.ToHashSet();
            var removals = _active.Keys
                .Where(key => !targetSet.Contains(key))
                .OrderByDescending(key => key.ManhattanDistanceTo(focus))
                .ThenBy(key => key.Y)
                .ThenBy(key => key.X)
                .ToArray();
            var loads = targets.Where(coordinate => !_active.ContainsKey(coordinate)).ToArray();
            var completedOperations = 0;
            var totalOperations = removals.Length + loads.Length;
            if (totalOperations > 0)
            {
                options.Progress?.Invoke(completedOperations, totalOperations);
            }
            for (var index = 0; index < removals.Length; index++)
            {
                operation.Token.ThrowIfCancellationRequested();
                await ReleaseAsync(removals[index]);
                completedOperations++;
                options.Progress?.Invoke(completedOperations, totalOperations);
                if ((index + 1) % options.MaxUnloadsPerFrame == 0 && index + 1 < removals.Length)
                {
                    await NextFrameAsync(operation.Token);
                }
            }

            if (removals.Length > 0)
            {
                await NextFrameAsync(operation.Token);
                _source.PurgeIdleCache();
            }

            for (var index = 0; index < loads.Length; index++)
            {
                operation.Token.ThrowIfCancellationRequested();
                await LoadAsync(loads[index], operation.Token);
                completedOperations++;
                options.Progress?.Invoke(completedOperations, totalOperations);
                if ((index + 1) % options.MaxLoadsPerFrame == 0 && index + 1 < loads.Length)
                {
                    await NextFrameAsync(operation.Token);
                }
            }

            _lastFocus = focus;
            _lastRadius = options.Radius;
            UpdateMetrics();
        }
        finally
        {
            _gate.Release();
            if (ReferenceEquals(_focusChange, operation))
            {
                _focusChange = null;
            }
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
        _focusChange?.Cancel();
        await _gate.WaitAsync();
        try
        {
            foreach (var coordinate in _active.Keys.ToArray())
            {
                await ReleaseAsync(coordinate);
            }

            // Scene shutdown detaches the parent before _ExitTree callbacks
            // finish. Avoid querying a null SceneTree; queued chunk nodes and
            // owned resources are still released synchronously below.
            if (_parent.IsInsideTree() && _parent.GetTree() is { } tree)
            {
                await _parent.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            _source.PurgeIdleCache();
            await _source.DisposeAsync();
            await _lifetime.DisposeAsync();
            UpdateMetrics();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async ValueTask LoadAsync(ChunkCoordinate coordinate, CancellationToken cancellationToken)
    {
        var chunkLifetime = _lifetime.CreateChild($"Chunk:{coordinate.X},{coordinate.Y}");
        Node? node = null;
        try
        {
            node = await _source.InstantiateAsync(
                _source.Canonicalize(coordinate),
                chunkLifetime,
                cancellationToken);
            if (node is not Node2D node2D)
            {
                var actualType = node.GetType().Name;
                node.Free();
                node = null;
                throw new InvalidDataException(
                    $"World chunk ({coordinate.X},{coordinate.Y}) root must derive from Node2D, but produced {actualType}.");
            }

            var capturedNode = node;
            chunkLifetime.Defer(() => ReleaseNode(capturedNode));
            node2D.Position = new Vector2(coordinate.X * _chunkWidth, coordinate.Y * _chunkHeight);
            LXContextInjector.InitializeTree(node2D, _context(), chunkLifetime);

            _parent.AddChild(node2D);
            _active.Add(coordinate, new ActiveChunk(node2D, chunkLifetime));
            _context().Events.Publish(new WorldChunkLoaded(coordinate, node2D));
        }
        catch
        {
            await chunkLifetime.DisposeAsync();
            throw;
        }
    }

    private async ValueTask ReleaseAsync(ChunkCoordinate coordinate)
    {
        if (_active.Remove(coordinate, out var chunk))
        {
            await chunk.Lifetime.DisposeAsync();
            _context().Events.Publish(new WorldChunkUnloaded(coordinate));
        }
    }

    private async ValueTask NextFrameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_parent.IsInsideTree() && _parent.GetTree() is { } tree)
        {
            await _parent.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private void UpdateMetrics() => _metrics.SetGauge("world.chunks_active", _active.Count);

    private static void ReleaseNode(Node node)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        if (node.IsInsideTree())
        {
            node.QueueFree();
        }
        else
        {
            node.Free();
        }
    }

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("World chunks must be managed from Godot's main thread.");
        }
    }
}

public readonly record struct WorldChunkLoaded(ChunkCoordinate Coordinate, Node Node);

public readonly record struct WorldChunkUnloaded(ChunkCoordinate Coordinate);
