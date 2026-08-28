using LX.Core.Flow;
using LX.Core.Lifetime;

namespace LXFramework.Core.Tests;

public sealed class GameFlowTests
{
    [Fact]
    public async Task Transition_ExitsAndCleansPreviousStateBeforeEnteringNext()
    {
        var log = new List<string>();
        await using var root = new LifetimeScope("test");
        await using var flow = new GameFlow<string, List<string>>(log, root);
        flow.Register("menu", new RecordingState("menu"));
        flow.Register("play", new RecordingState("play"));

        await flow.TransitionAsync("menu");
        await flow.TransitionAsync("play");

        Assert.Equal(
            ["enter:menu", "exit:menu", "cleanup:menu", "enter:play"],
            log);
    }

    [Fact]
    public async Task FailedEnter_LeavesNoCurrentStateAndCleansAttemptLifetime()
    {
        var log = new List<string>();
        await using var root = new LifetimeScope("test");
        await using var flow = new GameFlow<string, List<string>>(log, root);
        flow.Register("broken", new RecordingState("broken", failEnter: true));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await flow.TransitionAsync("broken"));

        Assert.False(flow.HasCurrent);
        Assert.Equal(["enter:broken", "cleanup:broken"], log);
    }

    [Fact]
    public async Task Dispose_CleansStateLifetimeWhenExitFails()
    {
        var log = new List<string>();
        await using var root = new LifetimeScope("test");
        var flow = new GameFlow<string, List<string>>(log, root);
        flow.Register("broken", new RecordingState("broken", failExit: true));
        await flow.TransitionAsync("broken");

        await Assert.ThrowsAsync<AggregateException>(async () => await flow.DisposeAsync());

        Assert.Equal(["enter:broken", "exit:broken", "cleanup:broken"], log);
    }

    private sealed class RecordingState(
        string name,
        bool failEnter = false,
        bool failExit = false) : IGameFlowState<List<string>>
    {
        public ValueTask EnterAsync(
            List<string> context,
            LifetimeScope lifetime,
            CancellationToken cancellationToken)
        {
            context.Add($"enter:{name}");
            lifetime.Defer(() => context.Add($"cleanup:{name}"));
            if (failEnter)
            {
                throw new InvalidOperationException("expected");
            }
            return ValueTask.CompletedTask;
        }

        public void Tick(List<string> context, double deltaSeconds) => context.Add($"tick:{name}");

        public ValueTask ExitAsync(List<string> context, CancellationToken cancellationToken)
        {
            context.Add($"exit:{name}");
            if (failExit)
            {
                throw new InvalidOperationException("expected");
            }
            return ValueTask.CompletedTask;
        }
    }
}
