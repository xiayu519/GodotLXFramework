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
}
