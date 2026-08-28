using LX.Res;
using LX.Core.Lifetime;
using LX.Core.Pooling;
using LX.Runtime;
using Godot;

namespace LX.Pooling;

/// <summary>
/// A local, lifetime-owned node pool backed by one PackedScene lease. Use one
/// pool per character/effect kind and own it by the narrowest world or battle
/// LifetimeScope.
/// </summary>
public sealed class PackedSceneNodePool<TNode> : IDisposable where TNode : Node
{
    private readonly AssetLease<PackedScene> _sceneLease;
    private readonly NodePool<TNode> _nodes;
    private readonly LXContext _context;
    private readonly LifetimeScope _ownerLifetime;
    private readonly Dictionary<TNode, LifetimeScope> _nodeLifetimes = new(ReferenceEqualityComparer.Instance);
    private long _nodeSequence;
    private bool _disposed;

    private PackedSceneNodePool(
        AssetLease<PackedScene> sceneLease,
        LXContext context,
        LifetimeScope ownerLifetime,
        Action<TNode>? reset,
        int maxRetained)
    {
        _sceneLease = sceneLease;
        _context = context;
        _ownerLifetime = ownerLifetime;
        _nodes = new NodePool<TNode>(CreateNode, reset, maxRetained, DisposeNode);
    }

    public int RetainedCount => _nodes.RetainedCount;

    public int RentedCount => _nodes.RentedCount;

    public PoolStatistics Statistics => _nodes.Statistics;

    public static ValueTask<PackedSceneNodePool<TNode>> CreateAsync(
        LXContext context,
        AssetRef<PackedScene> scene,
        LifetimeScope ownerLifetime,
        Action<TNode>? reset = null,
        int maxRetained = 64,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(
            context,
            scene.Path,
            scene.CachePolicy,
            ownerLifetime,
            reset,
            maxRetained,
            cancellationToken);

    public static ValueTask<PackedSceneNodePool<TNode>> CreateAsync(
        LXContext context,
        string scenePath,
        LifetimeScope ownerLifetime,
        Action<TNode>? reset = null,
        int maxRetained = 64,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(
            context,
            scenePath,
            AssetCachePolicy.Cached,
            ownerLifetime,
            reset,
            maxRetained,
            cancellationToken);

    public TNode Rent(Node parent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _nodes.Rent(parent);
    }

    public PooledNode<TNode> RentLease(Node parent, LifetimeScope? lifetime = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _nodes.RentLease(parent, lifetime);
    }

    public void Return(TNode node)
    {
        _nodes.Return(node);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _nodes.Dispose();
        foreach (var node in _nodeLifetimes.Keys.ToArray())
        {
            DisposeNode(node);
            if (node.GetParent() is not null)
            {
                node.GetParent().RemoveChild(node);
            }
            if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
            {
                node.QueueFree();
            }
        }
        _sceneLease.Dispose();
    }

    private TNode CreateNode()
    {
        var node = _sceneLease.Resource.Instantiate();
        if (node is not TNode typed)
        {
            var actualType = node.GetType().Name;
            node.Free();
            throw new InvalidOperationException(
                $"Pooled scene '{_sceneLease.Path}' must instantiate {typeof(TNode).Name}, but produced {actualType}.");
        }

        var lifetime = _ownerLifetime.CreateChild($"PooledNode:{++_nodeSequence}");
        try
        {
            LXContextInjector.InitializeTree(typed, _context, lifetime);
            _nodeLifetimes.Add(typed, lifetime);
            return typed;
        }
        catch
        {
            lifetime.Dispose();
            node.Free();
            throw;
        }
    }

    private void DisposeNode(TNode node)
    {
        if (_nodeLifetimes.Remove(node, out var lifetime))
        {
            lifetime.Dispose();
        }
    }

    private static async ValueTask<PackedSceneNodePool<TNode>> CreateCoreAsync(
        LXContext context,
        string scenePath,
        AssetCachePolicy cachePolicy,
        LifetimeScope ownerLifetime,
        Action<TNode>? reset,
        int maxRetained,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ownerLifetime);
        if (maxRetained <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetained));
        }

        var lease = await context.Res.AcquireAsync<PackedScene>(scenePath, cachePolicy, cancellationToken);
        try
        {
            return ownerLifetime.Own(new PackedSceneNodePool<TNode>(
                lease,
                context,
                ownerLifetime,
                reset,
                maxRetained));
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
