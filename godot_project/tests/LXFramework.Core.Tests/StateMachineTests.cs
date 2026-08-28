using LX.Core.Flow;

namespace LXFramework.Core.Tests;

public sealed class StateMachineTests
{
    [Fact]
    public async Task Transition_ExitsOldStateBeforeEnteringNewState()
    {
        var log = new List<string>();
        var machine = new StateMachine<string, List<string>>(log);
        machine.Register("idle", new ProbeState("idle"));
        machine.Register("active", new ProbeState("active"));

        await machine.TransitionAsync("idle");
        await machine.TransitionAsync("active");
        machine.Tick(0.25);

        Assert.Equal(["enter:idle", "exit:idle", "enter:active", "tick:active"], log);
        Assert.Equal("active", machine.Current);
    }

    [Fact]
    public async Task Transition_WhenNextEnterFails_LeavesNoCurrentState()
    {
        var log = new List<string>();
        var machine = new StateMachine<string, List<string>>(log);
        machine.Register("idle", new ProbeState("idle"));
        machine.Register("broken", new FailingState());

        await machine.TransitionAsync("idle");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await machine.TransitionAsync("broken"));

        Assert.False(machine.HasCurrent);
        Assert.Null(machine.Current);
        machine.Tick(0.25);
        Assert.Equal(["enter:idle", "exit:idle", "enter:broken"], log);
    }

    private sealed class ProbeState(string name) : IState<List<string>>
    {
        public ValueTask EnterAsync(List<string> context, CancellationToken cancellationToken)
        {
            context.Add($"enter:{name}");
            return ValueTask.CompletedTask;
        }

        public void Tick(List<string> context, double deltaSeconds) => context.Add($"tick:{name}");

        public ValueTask ExitAsync(List<string> context, CancellationToken cancellationToken)
        {
            context.Add($"exit:{name}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingState : IState<List<string>>
    {
        public ValueTask EnterAsync(List<string> context, CancellationToken cancellationToken)
        {
            context.Add("enter:broken");
            throw new InvalidOperationException("broken state");
        }

        public void Tick(List<string> context, double deltaSeconds) => context.Add("tick:broken");
    }
}
