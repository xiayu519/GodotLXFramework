using LX.Runtime;
using LX.Core.Lifetime;
using LX.Pooling;

namespace LX.Validation;

public partial class ContextProbeNode : LXNode, IPooledNodeLifecycle
{
    public bool InitializationHookCalled { get; private set; }

    public bool ChildrenWereInitializedFirst { get; private set; }

    public ICollection<string>? InitializationOrder { get; set; }

    public CancellationToken LastPoolActivationToken { get; private set; }

    public int PoolRentCount { get; private set; }

    public int PoolReturnCount { get; private set; }

    public bool PoolReturnObservedActiveToken { get; private set; }

    public bool ConfiguredBeforeTree { get; set; }

    protected override void OnLXInitialized()
    {
        ChildrenWereInitializedFirst = true;
        var childCount = GetChildCount();
        for (var index = 0; index < childCount; index++)
        {
            if (GetChild(index) is ILXContextReceiver { IsLXInitialized: false })
            {
                ChildrenWereInitializedFirst = false;
                break;
            }
        }
        InitializationOrder?.Add(Name);
        InitializationHookCalled = true;
    }

    public void OnRent(LifetimeScope activation)
    {
        LastPoolActivationToken = activation.Token;
        PoolRentCount++;
    }

    public void OnReturn()
    {
        PoolReturnObservedActiveToken = !LastPoolActivationToken.IsCancellationRequested;
        PoolReturnCount++;
    }
}
