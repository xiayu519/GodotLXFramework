using Godot;

namespace LX.Res;

/// <summary>
/// Represents one LX.Res ownership claim. Resource must not be retained by a
/// longer-lived target after this lease is released.
/// </summary>
public sealed class AssetLease<T> : IDisposable where T : Resource
{
    private AssetRegistry? _owner;
    private T? _resource;

    internal AssetLease(AssetRegistry owner, string path, T resource)
    {
        _owner = owner;
        Path = path;
        _resource = resource;
    }

    public string Path { get; }

    public bool IsDisposed => _owner is null;

    /// <summary>租约有效期内可使用的 Godot Resource。</summary>
    public T Resource => _resource ??
        throw new ObjectDisposedException(nameof(AssetLease<T>), $"Asset lease '{Path}' is already released.");

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        var resource = Interlocked.Exchange(ref _resource, null);
        if (owner is not null && resource is not null)
        {
            owner.Release(Path, resource);
        }
    }
}
