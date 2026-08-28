using Godot;

namespace LX.Features;

public sealed class FeatureHandle : IAsyncDisposable
{
    private FeatureService? _owner;
    private readonly Guid _instanceId;

    internal FeatureHandle(FeatureService owner, Guid instanceId, FeatureId featureId, Node node)
    {
        _owner = owner;
        _instanceId = instanceId;
        FeatureId = featureId;
        Node = node;
    }

    public FeatureId FeatureId { get; }

    public Node Node { get; }

    public bool IsDisposed => _owner is null;

    public async ValueTask DisposeAsync()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is not null)
        {
            await owner.DespawnAsync(_instanceId);
        }
    }
}
