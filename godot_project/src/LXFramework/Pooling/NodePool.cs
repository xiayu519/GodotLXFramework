using LX.Core.Pooling;
using LX.Core.Lifetime;
using Godot;

namespace LX.Pooling;

public sealed class NodePool<TNode> : IDisposable where TNode : Node
{
    private readonly ObjectPool<TNode> _pool;
    private readonly int _mainThreadId;

    public NodePool(
        Func<TNode> factory,
        Action<TNode>? reset = null,
        int maxRetained = 64,
        Action<TNode>? beforeDiscard = null)
    {
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        _pool = new ObjectPool<TNode>(
            factory,
            node =>
            {
                if (node.GetParent() is not null)
                {
                    node.GetParent().RemoveChild(node);
                }

                reset?.Invoke(node);
            },
            node =>
            {
                if (node.GetParent() is not null)
                {
                    node.GetParent().RemoveChild(node);
                }
                beforeDiscard?.Invoke(node);
                if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
                {
                    node.QueueFree();
                }
            },
            maxRetained);
    }

    public int RetainedCount
    {
        get
        {
            EnsureMainThread();
            return _pool.RetainedCount;
        }
    }

    public int RentedCount
    {
        get
        {
            EnsureMainThread();
            return _pool.RentedCount;
        }
    }

    public PoolStatistics Statistics
    {
        get
        {
            EnsureMainThread();
            return _pool.Statistics;
        }
    }

    public TNode Rent(Node parent)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(parent);
        var node = _pool.Rent();
        parent.AddChild(node);
        return node;
    }

    public PooledNode<TNode> RentLease(Node parent, LifetimeScope? lifetime = null)
    {
        var lease = new PooledNode<TNode>(this, Rent(parent));
        return lifetime is null ? lease : lifetime.Own(lease);
    }

    public void Return(TNode node)
    {
        EnsureMainThread();
        _pool.Return(node);
    }

    public void Dispose()
    {
        EnsureMainThread();
        _pool.Dispose();
    }

    private void EnsureMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThreadId)
        {
            throw new InvalidOperationException("Node pool operations must run on Godot's main thread.");
        }
    }
}

public sealed class PooledNode<TNode> : IDisposable where TNode : Node
{
    private NodePool<TNode>? _pool;

    internal PooledNode(NodePool<TNode> pool, TNode node)
    {
        _pool = pool;
        Node = node;
    }

    public TNode Node { get; }

    public bool IsReturned => _pool is null;

    public void Dispose() => Interlocked.Exchange(ref _pool, null)?.Return(Node);
}
