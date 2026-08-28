using Godot;

namespace LX.Res;

public sealed record AssetLoadRequest<T>(
    string Id,
    AssetRef<T> Asset,
    int Priority = 0,
    IReadOnlyList<string>? Dependencies = null)
    where T : Resource;

public sealed record AssetLoadProgress(
    int Completed,
    int Total,
    string? CurrentId)
{
    public float Ratio => Total == 0 ? 1 : (float)Completed / Total;
}

public sealed class AssetBatchLease<T> : IDisposable where T : Resource
{
    private readonly Dictionary<string, AssetLease<T>> _leases;
    private readonly IReadOnlyList<string> _acquisitionOrder;
    private bool _disposed;

    internal AssetBatchLease(
        Dictionary<string, AssetLease<T>> leases,
        IReadOnlyList<string> acquisitionOrder)
    {
        _leases = leases;
        _acquisitionOrder = acquisitionOrder;
    }

    public int Count => _leases.Count;

    public IEnumerable<string> Ids => _leases.Keys;

    public T this[string id]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _leases.TryGetValue(id, out var lease)
                ? lease.Resource
                : throw new KeyNotFoundException($"Asset batch does not contain '{id}'.");
        }
    }

    public bool TryGet(string id, out T? resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_leases.TryGetValue(id, out var lease))
        {
            resource = lease.Resource;
            return true;
        }

        resource = null;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var index = _acquisitionOrder.Count - 1; index >= 0; index--)
        {
            _leases[_acquisitionOrder[index]].Dispose();
        }
        _leases.Clear();
    }
}
