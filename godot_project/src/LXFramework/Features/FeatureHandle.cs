using Godot;

namespace LX.Features;

public sealed class FeatureHandle : IAsyncDisposable
{
    private FeatureService? _owner;
    private readonly Guid _instanceId;
    private readonly CancellationToken _instanceToken;

    internal FeatureHandle(
        FeatureService owner,
        Guid instanceId,
        FeatureId featureId,
        Node node,
        CancellationToken instanceToken)
    {
        _owner = owner;
        _instanceId = instanceId;
        _instanceToken = instanceToken;
        FeatureId = featureId;
        Node = node;
    }

    public FeatureId FeatureId { get; }

    public Node Node { get; }

    /// <summary>实例是否已由句柄、FeatureService 或所属生命周期释放。</summary>
    public bool IsDisposed => _owner is null || _instanceToken.IsCancellationRequested;

    public async ValueTask DisposeAsync()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is not null)
        {
            await owner.DespawnAsync(_instanceId);
        }
    }
}
