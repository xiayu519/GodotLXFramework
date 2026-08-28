using LX.Runtime;

namespace LX.Validation;

public partial class ContextProbeNode : LXNode
{
    public bool InitializationHookCalled { get; private set; }

    public bool ChildrenWereInitializedFirst { get; private set; }

    protected override void OnLXInitialized()
    {
        ChildrenWereInitializedFirst = GetChildren()
            .OfType<ILXContextReceiver>()
            .All(receiver => receiver.IsLXInitialized);
        InitializationHookCalled = true;
    }
}
