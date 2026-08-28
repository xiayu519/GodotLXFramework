using LX.Core.Lifetime;

namespace LX.Runtime;

public interface ILXContextReceiver
{
    bool IsLXInitialized { get; }

    void Initialize(LXContext context, LifetimeScope lifetime);
}
