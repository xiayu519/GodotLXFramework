using Godot;
using LX.UI;

namespace LX.Validation;

internal static class UIBackContractSmoke
{
    internal static async Task RunAsync(UIService ui)
    {
        foreach (var cached in new[] { false, true })
        foreach (var directClose in new[] { false, true })
        foreach (var delayedHide in new[] { false, true })
        {
            var id = new UIId(cached ? "contract.cached" : "contract.popup");
            var state = new UIContractProbeState { WaitInHide = delayedHide };
            var handle = await ui.OpenAsync(id, state);
            // Inline continuations reproduce a product payload waking an await-using owner.
            var payload = new TaskCompletionSource();
            var owner = directClose ? Task.CompletedTask : ConsumePayloadAsync();
            state.BackHandler = () =>
            {
                if (directClose) state.Screen.CloseFromProbe();
                else payload.SetResult();
                return ValueTask.FromResult(true);
            };
            var back = ui.RequestBackAsync().AsTask();
            await state.Hiding.Task;
            var waitedForHide = !delayedHide || !back.IsCompleted;
            state.HideReleased.TrySetResult();
            var handled = await back;
            await owner;
            Require(handled && waitedForHide && handle.IsClosed && state.HideCount == 1,
                $"Accepted Back lost or did not await self-close (cached={cached}, direct={directClose}, delayed={delayedHide}).");

            async Task ConsumePayloadAsync()
            {
                await using var owned = handle;
                await payload.Task;
            }
        }

        foreach (var failCleanup in new[] { false, true })
        foreach (var delayedHide in new[] { false, true })
        {
            var state = new UIContractProbeState { FailInHide = failCleanup, WaitInHide = delayedHide };
            var handle = await ui.OpenAsync(new UIId("contract.cached"), state);
            Task? closing = null;
            UIHandle? reopened = null;
            state.BackHandler = async () =>
            {
                closing = handle.CloseAsync().AsTask();
                if (!delayedHide)
                {
                    try { await closing; }
                    catch (AggregateException exception) when (IsHideFailure(exception)) { }
                    // Reusing the node must not reuse the old activation's close task.
                    reopened = await ui.OpenAsync(handle.UIId, new UIContractProbeState());
                }
                return true;
            };
            var back = ui.RequestBackAsync().AsTask();
            await state.Hiding.Task;
            var waitedForHide = !delayedHide || !back.IsCompleted;
            state.HideReleased.TrySetResult();
            var observedFailure = false;
            try
            {
                Require(await back, "Accepted Back was lost after cached reopen.");
            }
            catch (AggregateException exception) when (IsHideFailure(exception)) { observedFailure = true; }
            try { await closing!; }
            catch (AggregateException exception) when (IsHideFailure(exception)) { }
            Require(waitedForHide && observedFailure == failCleanup && state.HideCount == 1,
                "Back did not await or propagate the captured activation's cleanup outcome.");
            if (reopened is not null)
            {
                Require(!reopened.IsClosed && ui.IsOpen(reopened.UIId), "Old Back closed a newer cached activation.");
                await reopened.CloseAsync();
            }
        }

        var refusing = new UIContractProbeState { BackHandler = () => ValueTask.FromResult(false) };
        var refused = await ui.OpenAsync(new UIId("contract.popup"), refusing);
        Require(!await ui.RequestBackAsync() && !refused.IsClosed && refusing.HideCount == 0,
            "Refused Back closed the page or reported success.");
        await refused.CloseAsync();
        Require(!await ui.RequestBackAsync(), "Back reported success without an eligible page.");
        GD.Print("LX_UI_BACK_REENTRANCY_PASS");
    }

    private static bool IsHideFailure(AggregateException exception) =>
        exception.Flatten().InnerExceptions.Any(error => error.Message == "probe-hide-failure");

    private static void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
    }
}
