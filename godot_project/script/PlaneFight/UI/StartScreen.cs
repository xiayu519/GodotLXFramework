using Godot;
using LX.UI;

namespace PlaneFight.UI;

public partial class StartScreen : UIScreen
{
    protected internal override ValueTask OnShowAsync(object? payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartButton.Pressed += ChooseStart;
        ExitButton.Pressed += ChooseExit;
        Activation.Defer(() => StartButton.Pressed -= ChooseStart);
        Activation.Defer(() => ExitButton.Pressed -= ChooseExit);
        StartButton.GrabFocus();
        return ValueTask.CompletedTask;
    }

    protected internal override ValueTask<bool> OnBackRequestedAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    internal void ChooseForSmoke(StartChoice choice)
    {
        if (choice == StartChoice.Start)
        {
            ChooseStart();
            return;
        }
        ChooseExit();
    }

    private void ChooseStart() => RequestClose(StartChoice.Start);

    private void ChooseExit() => RequestClose(StartChoice.Exit);
}
