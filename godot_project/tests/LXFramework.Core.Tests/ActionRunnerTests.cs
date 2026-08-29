using LX.Core.Actions;
using LX.Core.Lifetime;

namespace LXFramework.Core.Tests;

public sealed class ActionRunnerTests
{
    [Fact]
    public async Task SequenceRunsInDeclaredOrderAndRetainsRecentSnapshot()
    {
        await using var lifetime = new LifetimeScope("test");
        await using var runner = new ActionRunner(lifetime);
        var order = new List<int>();

        await runner.RunAsync(
            LXActions.Sequence(
                LXActions.Invoke(() => order.Add(1), "first"),
                LXActions.Invoke(() => order.Add(2), "second")),
            lifetime);

        Assert.Equal([1, 2], order);
        var root = Assert.Single(runner.Snapshot().Recent);
        Assert.Equal(ActionNodeState.Completed, root.State);
        Assert.Equal(["first", "second"], root.Children.Select(child => child.Name));
    }

    [Fact]
    public async Task ParallelCancelsSiblingAfterFailure()
    {
        await using var lifetime = new LifetimeScope("test");
        await using var runner = new ActionRunner(lifetime);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = LXActions.Async(async token =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        }, "waiting");

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            LXActions.Parallel(
                waiting,
                LXActions.Async(_ => throw new InvalidOperationException("failed"), "failure")),
            lifetime));

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(ActionNodeState.Failed, Assert.Single(runner.Snapshot().Recent).State);
    }

    [Fact]
    public async Task RaceReturnsWinnerAndCancelsLoser()
    {
        await using var lifetime = new LifetimeScope("test");
        await using var runner = new ActionRunner(lifetime);
        var loserCancelled = false;

        await runner.RunAsync(
            LXActions.Race(
                LXActions.Delay(TimeSpan.FromMilliseconds(5), "winner"),
                LXActions.Async(async token =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), token);
                    }
                    catch (OperationCanceledException)
                    {
                        loserCancelled = true;
                        throw;
                    }
                }, "loser")),
            lifetime);

        Assert.True(loserCancelled);
    }

    [Fact]
    public async Task RetryEventuallyCompletes()
    {
        await using var lifetime = new LifetimeScope("test");
        await using var runner = new ActionRunner(lifetime);
        var attempts = 0;
        var action = LXActions.Async(_ =>
        {
            attempts++;
            return attempts < 3
                ? ValueTask.FromException(new InvalidOperationException("retry"))
                : ValueTask.CompletedTask;
        }, "unstable");

        await runner.RunAsync(LXActions.Retry(action, 3), lifetime);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task TimeoutReportsFailureAndOwnerCancellationReportsCancellation()
    {
        await using var lifetime = new LifetimeScope("test");
        await using var runner = new ActionRunner(lifetime);
        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            LXActions.Timeout(
                LXActions.Delay(TimeSpan.FromSeconds(30)),
                TimeSpan.FromMilliseconds(10)),
            lifetime));
        Assert.Equal(ActionNodeState.Failed, Assert.Single(runner.Snapshot().Recent).State);

        var owner = lifetime.CreateChild("owner");
        var pending = runner.RunAsync(LXActions.Delay(TimeSpan.FromSeconds(30)), owner);
        await owner.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(ActionNodeState.Cancelled, runner.Snapshot().Recent.Last().State);
    }

    [Fact]
    public async Task FinallyRunsCleanupOnFailure()
    {
        await using var lifetime = new LifetimeScope("test");
        await using var runner = new ActionRunner(lifetime);
        var cleaned = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            LXActions.Finally(
                LXActions.Async(_ => throw new InvalidOperationException("failed")),
                LXActions.Invoke(() => cleaned = true)),
            lifetime));

        Assert.True(cleaned);
    }

    [Fact]
    public async Task FinallyRunsCleanupWhenOwnerIsCancelled()
    {
        await using var lifetime = new LifetimeScope("test");
        await using var runner = new ActionRunner(lifetime);
        var owner = lifetime.CreateChild("owner");
        var cleaned = false;
        var running = runner.RunAsync(
            LXActions.Finally(
                LXActions.Delay(TimeSpan.FromSeconds(30)),
                LXActions.Invoke(() => cleaned = true)),
            owner);

        await owner.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        Assert.True(cleaned);
    }

    [Fact]
    public async Task SynchronousDisposeCancelsStartupRaceAndAsyncDisposeWaitsForSameShutdown()
    {
        await using var lifetime = new LifetimeScope("test");
        var runner = new ActionRunner(lifetime);
        var running = runner.RunAsync(LXActions.Delay(TimeSpan.FromSeconds(30)), lifetime);

        runner.Dispose();
        var shutdown = runner.DisposeAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await shutdown;
        Assert.Empty(runner.Snapshot().Active);
        Assert.Equal(ActionNodeState.Cancelled, Assert.Single(runner.Snapshot().Recent).State);
    }
}
