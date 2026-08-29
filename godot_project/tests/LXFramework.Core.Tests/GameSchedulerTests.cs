using LX.Core.Time;

namespace LXFramework.Core.Tests;

public sealed class GameSchedulerTests
{
    [Fact]
    public void Tick_DefersZeroDelayWorkScheduledByCallbackUntilNextTick()
    {
        var clock = new GameClock();
        using var scheduler = new GameScheduler(clock);
        var calls = 0;
        Action? callback = null;
        callback = () =>
        {
            calls++;
            scheduler.Schedule(TimeSpan.Zero, callback!);
        };
        scheduler.Schedule(TimeSpan.Zero, callback);

        scheduler.Tick();
        Assert.Equal(1, calls);

        scheduler.Tick();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CancelledItem_IsRemovedFromPendingCount()
    {
        var clock = new GameClock();
        using var scheduler = new GameScheduler(clock);
        var handle = scheduler.Schedule(TimeSpan.FromDays(1), () => { });

        handle.Dispose();

        Assert.Equal(0, scheduler.PendingCount);
    }
}
