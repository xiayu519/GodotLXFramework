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

    [Fact]
    public async Task ParentOwnership_ExitsStateBeforeRootCleanupCompletes()
    {
        var log = new List<string>();
        var root = new LifetimeScope("test");
        var flow = root.Own(new GameFlow<string, List<string>>(log, root));
        flow.Register("play", new RecordingState("play"));
        await flow.TransitionAsync("play");

        await root.DisposeAsync();

        Assert.Equal(["enter:play", "exit:play", "cleanup:play"], log);
        Assert.False(flow.HasCurrent);
    }

    [Fact]
    public async Task Transition_ReenteredFromStateHook_FailsImmediatelyInsteadOfDeadlocking()
    {
        await using var root = new LifetimeScope("test");
        var context = new ReentrantContext();
        await using var flow = new GameFlow<string, ReentrantContext>(context, root);
        context.Flow = flow;
        flow.Register("first", new ReentrantState("second"));
        flow.Register("second", new PassiveState());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await flow.TransitionAsync("first"));

        Assert.Contains("cannot be re-entered", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(flow.HasCurrent);
    }

    [Fact]
    public async Task Transition_WhenExitFails_RetainsCurrentStateForDeterministicRetryOrDispose()
    {
        var log = new List<string>();
        await using var root = new LifetimeScope("test");
        var flow = new GameFlow<string, List<string>>(log, root);
        flow.Register("broken", new RecordingState("broken", failExit: true));
        flow.Register("next", new RecordingState("next"));
        await flow.TransitionAsync("broken");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await flow.TransitionAsync("next"));

        Assert.True(flow.HasCurrent);
        Assert.Equal("broken", flow.Current);
        await Assert.ThrowsAsync<AggregateException>(async () => await flow.DisposeAsync());
        Assert.Equal(["enter:broken", "exit:broken", "exit:broken", "cleanup:broken"], log);
    }

    [Fact]
    public async Task Transition_WhenPreviousLifetimeCleanupFails_LeavesNoPhantomCurrentState()
    {
        await using var root = new LifetimeScope("test");
        await using var flow = new GameFlow<string, object>(new object(), root);
        flow.Register("dirty", new CleanupFailingState());
        flow.Register("next", new PassiveObjectState());
        await flow.TransitionAsync("dirty");

        await Assert.ThrowsAsync<AggregateException>(async () =>
            await flow.TransitionAsync("next"));

        Assert.False(flow.HasCurrent);
        await flow.TransitionAsync("next");
        Assert.Equal("next", flow.Current);
    }

    [Fact]
    public async Task TransitionedObserverFailuresAreIsolatedAndReported()
    {
        await using var root = new LifetimeScope("test");
        await using var flow = new GameFlow<string, object>(new object(), root);
        flow.Register("next", new PassiveObjectState());
        var completedObserver = false;
        Exception? observedFailure = null;
        flow.Transitioned += _ => throw new InvalidOperationException("observer failed");
        flow.Transitioned += _ => completedObserver = true;
        flow.TransitionObserverFailed += exception => observedFailure = exception;

        await flow.TransitionAsync("next");

        Assert.True(completedObserver);
        Assert.IsType<InvalidOperationException>(observedFailure);
        Assert.Equal("next", flow.Current);
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

    private sealed class ReentrantContext
    {
        public GameFlow<string, ReentrantContext> Flow { get; set; } = null!;
    }

    private sealed class ReentrantState(string next) : IGameFlowState<ReentrantContext>
    {
        public async ValueTask EnterAsync(
            ReentrantContext context,
            LifetimeScope lifetime,
            CancellationToken cancellationToken)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await context.Flow.TransitionAsync(next, timeout.Token);
        }

        public void Tick(ReentrantContext context, double deltaSeconds)
        {
        }
    }

    private sealed class PassiveState : IGameFlowState<ReentrantContext>
    {
        public void Tick(ReentrantContext context, double deltaSeconds)
        {
        }
    }

    private sealed class CleanupFailingState : IGameFlowState<object>
    {
        public ValueTask EnterAsync(
            object context,
            LifetimeScope lifetime,
            CancellationToken cancellationToken)
        {
            lifetime.Defer(() => throw new InvalidOperationException("cleanup failed"));
            return ValueTask.CompletedTask;
        }

        public void Tick(object context, double deltaSeconds)
        {
        }
    }

    private sealed class PassiveObjectState : IGameFlowState<object>
    {
        public void Tick(object context, double deltaSeconds)
        {
        }
    }
}
