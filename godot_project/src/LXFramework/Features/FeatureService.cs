using LX.Res;
using LX.Core.Diagnostics;
using LX.Core.Lifetime;
using LX.Runtime;
using Godot;

namespace LX.Features;

public sealed class FeatureService : IAsyncDisposable
{
    private sealed record FeatureInstance(
        Guid InstanceId,
        FeatureDescriptor Descriptor,
        Node Node,
        LifetimeScope Lifetime);

    private readonly AssetRegistry _assets;
    private readonly LifetimeScope _rootLifetime;
    private readonly MetricRegistry _metrics;
    private readonly Func<LXContext> _context;
    private readonly Dictionary<FeatureId, FeatureDescriptor> _catalog = [];
    private readonly Dictionary<Guid, FeatureInstance> _active = [];
    private readonly int _mainThreadId;
    private bool _disposed;

    public FeatureService(
        AssetRegistry assets,
        LifetimeScope rootLifetime,
        MetricRegistry metrics,
        Func<LXContext> context)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _rootLifetime = rootLifetime ?? throw new ArgumentNullException(nameof(rootLifetime));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        UpdateMetrics();
    }

    public void Register(FeatureDescriptor descriptor)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        descriptor.Validate();
        if (!_catalog.TryAdd(descriptor.Id, descriptor))
        {
            throw new InvalidOperationException($"Feature ID '{descriptor.Id}' is already registered.");
        }
    }

    public void RegisterRange(IEnumerable<FeatureDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        foreach (var descriptor in descriptors)
        {
            Register(descriptor);
        }
    }

    public IReadOnlyList<FeatureRecord> Snapshot()
    {
        EnsureMainThread();
        return _active.Values
            .OrderBy(instance => instance.Descriptor.Id.Value, StringComparer.Ordinal)
            .ThenBy(instance => instance.InstanceId)
            .Select(instance => new FeatureRecord(
                instance.InstanceId,
                instance.Descriptor.Id.Value,
                GodotObject.IsInstanceValid(instance.Node)
                    ? instance.Node.Name.ToString()
                    : "<freed>"))
            .ToArray();
    }

    public async ValueTask<FeatureHandle> SpawnAsync(
        FeatureId featureId,
        Node parent,
        LifetimeScope? parentLifetime = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parent);
        if (!_catalog.TryGetValue(featureId, out var descriptor))
        {
            throw new KeyNotFoundException($"Feature '{featureId}' is not registered.");
        }

        var lease = await _assets.AcquireAsync<PackedScene>(
            descriptor.ScenePath,
            AssetCachePolicy.Transient,
            cancellationToken);
        var instanceId = Guid.NewGuid();
        var ownerLifetime = parentLifetime ?? _rootLifetime;
        var lifetime = ownerLifetime.CreateChild($"Feature:{featureId.Value}:{instanceId:N}");
        Node? node = null;
        try
        {
            lifetime.Own(lease);
            node = lease.Resource.Instantiate();
            var capturedNode = node;
            lifetime.Defer(() =>
            {
                if (GodotObject.IsInstanceValid(capturedNode))
                {
                    capturedNode.QueueFree();
                }
            });
            LXContextInjector.InitializeTree(node, _context(), lifetime);
            parent.AddChild(node);

            var instance = new FeatureInstance(instanceId, descriptor, node, lifetime);
            _active.Add(instanceId, instance);
            var cancelWithOwner = ownerLifetime.Token.Register(() =>
                Callable.From((Action)(() => _ = DespawnSafelyAsync(instanceId))).CallDeferred());
            lifetime.Own(cancelWithOwner);
            UpdateMetrics();
            return new FeatureHandle(this, instanceId, featureId, node);
        }
        catch
        {
            await lifetime.DisposeAsync();
            throw;
        }
    }

    internal async ValueTask DespawnAsync(Guid instanceId)
    {
        EnsureMainThread();
        if (_active.Remove(instanceId, out var instance))
        {
            await instance.Lifetime.DisposeAsync();
            UpdateMetrics();
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
        foreach (var instanceId in _active.Keys.ToArray())
        {
            await DespawnAsync(instanceId);
        }
        _catalog.Clear();
        UpdateMetrics();
    }

    private async Task DespawnSafelyAsync(Guid instanceId)
    {
        try
        {
            await DespawnAsync(instanceId);
        }
        catch (Exception exception)
        {
            GD.PushError($"Failed to despawn feature {instanceId}: {exception}");
        }
    }

    private void UpdateMetrics() => _metrics.SetGauge("features.active", _active.Count);

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Features must be managed from Godot's main thread.");
        }
    }
}

public sealed record FeatureRecord(Guid InstanceId, string FeatureId, string NodeName);
