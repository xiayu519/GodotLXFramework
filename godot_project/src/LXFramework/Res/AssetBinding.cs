using LX.Core.Lifetime;
using Godot;

namespace LX.Res;

/// <summary>
/// Owns one dynamically replaceable resource property. The target reference is
/// cleared before the active lease is released, so the target and LX.Res share
/// one explicit lifetime boundary.
/// </summary>
public sealed class AssetBinding<T> : IDisposable where T : Resource
{
    private readonly AssetRegistry _assets;
    private readonly CancellationToken _lifetimeToken;
    private Action<T?>? _apply;
    private AssetLease<T>? _lease;
    private long _requestVersion;
    private bool _disposed;

    private AssetBinding(
        AssetRegistry assets,
        CancellationToken lifetimeToken,
        Action<T?> apply)
    {
        _assets = assets;
        _lifetimeToken = lifetimeToken;
        _apply = apply;
    }

    /// <summary>当前绑定的资源；未绑定或已释放时为 null。</summary>
    public T? Resource => _lease?.Resource;

    /// <summary>当前是否持有资源租约。</summary>
    public bool HasValue => _lease is not null;

    /// <summary>绑定是否已经随所属生命周期释放。</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// 创建并立即交给指定生命周期持有。apply 必须只把资源赋给同一生命周期内的目标。
    /// </summary>
    public static AssetBinding<T> Create(
        AssetRegistry assets,
        LifetimeScope lifetime,
        Action<T?> apply)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(apply);
        assets.EnsureMainThread();
        lifetime.ThrowIfDisposed();
        return lifetime.Own(new AssetBinding<T>(assets, lifetime.Token, apply));
    }

    /// <summary>同步替换资源；先应用新资源，再归还旧资源租约。</summary>
    public void Set(AssetRef<T> asset)
    {
        EnsureUsable();
        var version = ++_requestVersion;
        AssetLease<T>? next = null;
        try
        {
            next = _assets.Acquire(asset);
            if (_disposed || version != _requestVersion)
            {
                return;
            }

            _apply!(next.Resource);
            var previous = _lease;
            _lease = next;
            next = null;
            previous?.Dispose();
        }
        finally
        {
            next?.Dispose();
        }
    }

    /// <summary>
    /// 异步替换资源。多个请求交错完成时只有最后一次请求会生效，过期请求取得的租约会立即归还。
    /// </summary>
    /// <returns>true 表示本次资源已应用；false 表示它已被更新请求取代。</returns>
    public async ValueTask<bool> SetAsync(
        AssetRef<T> asset,
        CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        var version = ++_requestVersion;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeToken,
            cancellationToken);
        AssetLease<T>? next = null;
        try
        {
            next = await _assets.AcquireAsync(asset, operation.Token);
            _assets.EnsureMainThread();
            operation.Token.ThrowIfCancellationRequested();
            if (_disposed || version != _requestVersion)
            {
                return false;
            }

            _apply!(next.Resource);
            var previous = _lease;
            _lease = next;
            next = null;
            previous?.Dispose();
            return true;
        }
        finally
        {
            next?.Dispose();
        }
    }

    /// <summary>清空目标引用，然后归还当前资源租约。</summary>
    public void Clear()
    {
        _assets.EnsureMainThread();
        if (_disposed)
        {
            return;
        }

        _requestVersion++;
        var previous = _lease;
        _lease = null;
        try
        {
            _apply!(null);
        }
        finally
        {
            previous?.Dispose();
        }
    }

    public void Dispose()
    {
        _assets.EnsureMainThread();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requestVersion++;
        var apply = _apply;
        _apply = null;
        var previous = _lease;
        _lease = null;
        try
        {
            apply?.Invoke(null);
        }
        finally
        {
            previous?.Dispose();
        }
    }

    private void EnsureUsable()
    {
        _assets.EnsureMainThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lifetimeToken.ThrowIfCancellationRequested();
    }
}
