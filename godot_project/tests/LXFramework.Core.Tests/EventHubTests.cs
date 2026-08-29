using LX.Core.Events;
using LX.Core.Lifetime;

namespace LXFramework.Core.Tests;

public sealed class EventHubTests
{
    [Fact]
    public void ScopedSubscription_IsRemovedWhenScopeCloses()
    {
        using var events = new EventHub();
        var scope = new LifetimeScope("listener");
        var total = 0;
        events.Subscribe<int>(value => total += value, scope);

        events.Publish(3);
        scope.Dispose();
        events.Publish(5);

        Assert.Equal(3, total);
    }

    [Fact]
    public void Publish_UsesSnapshotWhenHandlerUnsubscribes()
    {
        using var events = new EventHub();
        var calls = 0;
        IDisposable? subscription = null;
        subscription = events.Subscribe<int>(_ =>
        {
            calls++;
            subscription!.Dispose();
        });

        events.Publish(1);
        events.Publish(1);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void IsolatedPublish_ReportsFailureAndContinuesDispatch()
    {
        var failures = new List<Exception>();
        using var events = new EventHub(failures.Add, isolateHandlerExceptions: true);
        var calls = 0;
        events.Subscribe<int>(_ => throw new InvalidOperationException("handler"));
        events.Subscribe<int>(_ => calls++);

        events.Publish(1);

        Assert.Equal(1, calls);
        Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(failures[0]);
    }

    [Fact]
    public void DefaultPublish_PreservesExceptionPropagationContract()
    {
        using var events = new EventHub();
        events.Subscribe<int>(_ => throw new InvalidOperationException("handler"));

        Assert.Throws<InvalidOperationException>(() => events.Publish(1));
    }

    [Fact]
    public void Publish_WithStableSubscription_AllocatesZeroBytes()
    {
        using var events = new EventHub();
        using var subscription = events.Subscribe<int>(static _ => { });
        for (var index = 0; index < 128; index++)
        {
            events.Publish(index);
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100_000; index++)
        {
            events.Publish(index);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0, allocated);
    }
}
