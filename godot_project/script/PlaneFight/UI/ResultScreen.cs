using Godot;
using LX.UI;

namespace PlaneFight.UI;

public partial class ResultScreen : UIScreen
{
    internal BattleOutcomeKind? DisplayedOutcome { get; private set; }

    protected internal override ValueTask OnShowAsync(object? payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (payload is not ResultScreenPayload result)
        {
            throw new ArgumentException(
                $"{nameof(ResultScreen)} requires a {nameof(ResultScreenPayload)} payload.",
                nameof(payload));
        }

        RestartButton.Pressed += ChooseRestart;
        ExitButton.Pressed += ChooseExit;
        Activation.Defer(() => RestartButton.Pressed -= ChooseRestart);
        Activation.Defer(() => ExitButton.Pressed -= ChooseExit);
        var victory = result.Outcome.Kind == BattleOutcomeKind.Victory;
        DisplayedOutcome = result.Outcome.Kind;
        EyebrowLabel.Text = victory ? "LEVEL 01 COMPLETE" : "MISSION FAILED";
        TitleLabel.Text = victory ? "第一关完成" : "战机被击落";
        MessageLabel.Text = victory
            ? "Boss 已被击破，航线恢复安全。"
            : "本次突击已经结束，整备后可重新挑战。";
        StatsLabel.Text =
            $"得分  {result.Outcome.Score}\n金币  {result.Outcome.Gold}    勋章  {result.Outcome.Medals}";
        RestartButton.GrabFocus();
        return ValueTask.CompletedTask;
    }

    protected internal override ValueTask<bool> OnBackRequestedAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }

    protected internal override ValueTask OnHideAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisplayedOutcome = null;
        return ValueTask.CompletedTask;
    }

    internal void ChooseForSmoke(ResultChoice choice)
    {
        if (choice == ResultChoice.Restart)
        {
            ChooseRestart();
            return;
        }
        ChooseExit();
    }

    private void ChooseRestart() => RequestClose(ResultChoice.Restart);

    private void ChooseExit() => RequestClose(ResultChoice.Exit);
}
