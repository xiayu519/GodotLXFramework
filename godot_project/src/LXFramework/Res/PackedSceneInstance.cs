using LX.Core.Lifetime;
using LX.Runtime;
using Godot;

namespace LX.Res;

/// <summary>
/// Owns one PackedScene instance, its injected LX lifetime, and the scene resource lease.
/// This is the Godot PackedScene equivalent of a one-off prefab handle; repeated instances
/// should use PackedSceneNodePool instead.
/// </summary>
public sealed class PackedSceneInstance<TNode> : IDisposable, IAsyncDisposable where TNode : Node
{
    private readonly AssetRegistry _assets;
    private LifetimeScope? _lifetime;
    private SceneTree? _tree;
    private int _disposed;

    private PackedSceneInstance(
        AssetRegistry assets,
        LifetimeScope lifetime,
        SceneTree? tree,
        TNode node)
    {
        _assets = assets;
        _lifetime = lifetime;
        _tree = tree;
        Node = node;
    }

    /// <summary>已实例化并完成 LX 上下文注入的根节点。</summary>
    public TNode Node { get; }

    /// <summary>只属于本次 PackedScene 实例的生命周期。</summary>
    public LifetimeScope Lifetime => _lifetime ??
        throw new ObjectDisposedException(nameof(PackedSceneInstance<TNode>));

    /// <summary>实例是否已经进入释放流程。</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0 || _lifetime?.IsDisposed != false;

    /// <summary>
    /// 从类型化资源目录创建一次性 PackedScene 实例，并立即交给 ownerLifetime 持有。
    /// </summary>
    public static async ValueTask<PackedSceneInstance<TNode>> CreateAsync(
        LXContext context,
        AssetRef<PackedScene> scene,
        Node parent,
        LifetimeScope ownerLifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(ownerLifetime);
        context.Res.EnsureMainThread();
        ownerLifetime.ThrowIfDisposed();

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            ownerLifetime.Token,
            cancellationToken);
        var lease = await context.Res.AcquireAsync(scene, operation.Token);
        context.Res.EnsureMainThread();
        operation.Token.ThrowIfCancellationRequested();

        Node? untyped = null;
        LifetimeScope? instanceLifetime = null;
        var cleanupRegistered = false;
        try
        {
            instanceLifetime = ownerLifetime.CreateChild(
                $"PackedScene:{typeof(TNode).Name}:{Guid.NewGuid():N}");
            instanceLifetime.Own(lease);
            untyped = lease.Resource.Instantiate();
            if (untyped is not TNode typed)
            {
                var actualType = untyped.GetType().Name;
                untyped.Free();
                untyped = null;
                throw new InvalidOperationException(
                    $"PackedScene '{scene.Path}' must instantiate {typeof(TNode).Name}, but produced {actualType}.");
            }

            var capturedNode = typed;
            instanceLifetime.Defer(() => ReleaseNode(capturedNode));
            cleanupRegistered = true;
            LXContextInjector.InitializeTree(typed, context, instanceLifetime);
            parent.AddChild(typed);
            return new PackedSceneInstance<TNode>(
                context.Res,
                instanceLifetime,
                parent.GetTree(),
                typed);
        }
        catch
        {
            instanceLifetime?.Dispose();
            if (!cleanupRegistered && untyped is not null && GodotObject.IsInstanceValid(untyped))
            {
                untyped.Free();
            }
            if (instanceLifetime is null)
            {
                lease.Dispose();
            }
            throw;
        }
    }

    /// <summary>
    /// 同步请求节点销毁并归还场景租约。需要确认 QueueFree 已生效时使用 DisposeAsync。
    /// </summary>
    public void Dispose()
    {
        _assets.EnsureMainThread();
        if (!TryBeginDispose(out var lifetime, out _))
        {
            return;
        }

        lifetime.Dispose();
    }

    /// <summary>
    /// 取消实例生命周期并归还 PackedScene 租约，然后等待 QueueFree 在下一帧完成。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _assets.EnsureMainThread();
        if (!TryBeginDispose(out var lifetime, out var tree))
        {
            return;
        }

        Exception? lifetimeError = null;
        try
        {
            await lifetime.DisposeAsync();
        }
        catch (Exception exception)
        {
            lifetimeError = exception;
        }

        if (GodotObject.IsInstanceValid(Node) &&
            Node.IsQueuedForDeletion() &&
            tree is not null &&
            GodotObject.IsInstanceValid(tree))
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        if (lifetimeError is not null)
        {
            throw lifetimeError;
        }
    }

    private bool TryBeginDispose(
        out LifetimeScope lifetime,
        out SceneTree? tree)
    {
        tree = null;
        lifetime = null!;
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return false;
        }

        lifetime = Interlocked.Exchange(ref _lifetime, null)!;
        tree = Interlocked.Exchange(ref _tree, null);
        return true;
    }

    private static bool ReleaseNode(TNode node)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return false;
        }

        if (node.IsInsideTree())
        {
            node.QueueFree();
            return true;
        }

        node.Free();
        return false;
    }
}
