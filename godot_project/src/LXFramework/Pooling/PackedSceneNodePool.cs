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
    private readonly LifetimeScope _poolLifetime;
    private readonly Action<TNode>? _reset;
    private readonly Dictionary<TNode, LifetimeScope> _nodeLifetimes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TNode, LifetimeScope> _activations = new(ReferenceEqualityComparer.Instance);
    private long _nodeSequence;
    private long _activationSequence;
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
        // The private lifetime is registered before this pool is registered on
        // ownerLifetime. Reverse-order disposal therefore reaches the pool first,
        // allowing OnReturn to observe a still-active activation token.
        _poolLifetime = ownerLifetime.CreateChild("PackedSceneNodePool");
        _reset = reset;
        _nodes = new NodePool<TNode>(CreateNode, ResetNode, maxRetained, DisposeNode);
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
        => Rent(parent, configure: null);

    /// <summary>配置节点并建立本次租用生命周期后，再把节点加入场景树。</summary>
    public TNode Rent(Node parent, Action<TNode>? configure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _nodes.Rent(parent, node =>
        {
            configure?.Invoke(node);
            BeginActivation(node);
        });
    }

    public PooledNode<TNode> RentLease(Node parent, LifetimeScope? lifetime = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _nodes.RentLease(parent, BeginActivation, lifetime);
    }

    /// <summary>配置节点后再入树，并把归还句柄绑定到指定生命周期。</summary>
    public PooledNode<TNode> RentLease(
        Node parent,
        Action<TNode> configure,
        LifetimeScope? lifetime = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(configure);
        return _nodes.RentLease(parent, node =>
        {
            configure(node);
            BeginActivation(node);
        }, lifetime);
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
        List<Exception>? errors = null;
        try
        {
            _nodes.Dispose();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }

        foreach (var node in _nodeLifetimes.Keys.ToArray())
        {
            try
            {
                DisposeNode(node);
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }

            try
            {
                if (!GodotObject.IsInstanceValid(node))
                {
                    continue;
                }
                if (node.GetParent() is { } parent)
                {
                    parent.RemoveChild(node);
                }
                if (!node.IsQueuedForDeletion())
                {
                    node.QueueFree();
                }
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        try
        {
            _sceneLease.Dispose();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        try
        {
            _poolLifetime.Dispose();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }

        if (errors is not null)
        {
            throw new AggregateException("Packed-scene node pool cleanup reported one or more errors.", errors);
        }
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

        var lifetime = _poolLifetime.CreateChild($"PooledNode:{++_nodeSequence}");
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
        Exception? activationError = null;
        try
        {
            EndActivation(node);
        }
        catch (Exception exception)
        {
            activationError = exception;
        }

        Exception? lifetimeError = null;
        if (_nodeLifetimes.Remove(node, out var lifetime))
        {
            try
            {
                lifetime.Dispose();
            }
            catch (Exception exception)
            {
                lifetimeError = exception;
            }
        }

        if (activationError is not null && lifetimeError is not null)
        {
            throw new AggregateException(
                "Pooled node activation and instance cleanup both failed.",
                activationError,
                lifetimeError);
        }
        if (activationError is not null)
        {
            throw activationError;
        }
        if (lifetimeError is not null)
        {
            throw lifetimeError;
        }
    }

    private void BeginActivation(TNode node)
    {
        if (node is not IPooledNodeLifecycle lifecycle)
        {
            return;
        }

        var activation = _nodeLifetimes[node].CreateChild($"Activation:{++_activationSequence}");
        _activations.Add(node, activation);
        try
        {
            lifecycle.OnRent(activation);
        }
        catch
        {
            _activations.Remove(node);
            activation.Dispose();
            throw;
        }
    }

    private void EndActivation(TNode node)
    {
        if (!_activations.Remove(node, out var activation))
        {
            return;
        }

        Exception? hookError = null;
        try
        {
            ((IPooledNodeLifecycle)node).OnReturn();
        }
        catch (Exception exception)
        {
            hookError = exception;
        }

        try
        {
            activation.Dispose();
        }
        catch (Exception disposalError) when (hookError is not null)
        {
            throw new AggregateException("Pooled node return and activation cleanup both failed.", hookError, disposalError);
        }

        if (hookError is not null)
        {
            throw hookError;
        }
    }

    private void ResetNode(TNode node)
    {
        EndActivation(node);
        _reset?.Invoke(node);
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

        ownerLifetime.ThrowIfDisposed();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            ownerLifetime.Token);
        var lease = await context.Res.AcquireAsync<PackedScene>(scenePath, cachePolicy, operation.Token);
        context.Res.EnsureMainThread();
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
