using LX.Core.Time;

namespace LXFramework.Core.Tests;

public sealed class TimeTests
{
    [Fact]
    public void Clock_SeparatesScaledAndUnscaledTime()
    {
        var clock = new GameClock { TimeScale = 0.5 };

        var frame = clock.Advance(2.0);

        Assert.Equal(2.0, frame.UnscaledElapsedSeconds);
        Assert.Equal(1.0, frame.ElapsedSeconds);
        Assert.Equal(1.0, frame.DeltaSeconds);
    }

    [Fact]
    public void Scheduler_UsesScaledGameTimeAndSupportsCancellation()
    {
        var clock = new GameClock();
        using var scheduler = new GameScheduler(clock);
        var calls = 0;
        scheduler.Schedule(TimeSpan.FromSeconds(1), () => calls++);
        var cancelled = scheduler.Schedule(TimeSpan.FromSeconds(1), () => calls += 100);
        cancelled.Dispose();

        clock.Advance(0.5);
        scheduler.Tick();
        Assert.Equal(0, calls);

        clock.Advance(0.5);
        scheduler.Tick();
        Assert.Equal(1, calls);
    }
}
