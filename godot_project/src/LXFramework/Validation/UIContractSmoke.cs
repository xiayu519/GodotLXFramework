using Godot;
using LX.Core.Diagnostics;
using LX.Core.Lifetime;
using LX.Runtime;
using LX.UI;

namespace LX.Validation;

internal static class UIContractSmoke
{
    internal static async Task RunAsync(Node host, LXContext original)
    {
        var baselineLeases = original.Res.Snapshot().Sum(item => item.LeaseCount);
        await using var lifetime = new LifetimeScope("UIContractFixture");
        var fixtureHost = new Node { Name = "UIContractFixture" };
        host.AddChild(fixtureHost);
        LXContext context = original;
        var ui = new UIService(fixtureHost, original.Res, lifetime, new MetricRegistry(), () => context);
        context = original with { UI = ui, Lifetime = lifetime };
        try
        {
            const string scene = "res://scene/validation/ui_contract_probe.tscn";
            foreach (var (id, layer, cache, modal) in new[]
            {
                ("contract.page_a", UILayer.Screen, UICachePolicy.Transient, false),
                ("contract.page_b", UILayer.Screen, UICachePolicy.Transient, false),
                ("contract.chrome", UILayer.Chrome, UICachePolicy.CachedSingleton, false),
                ("contract.popup", UILayer.Popup, UICachePolicy.Transient, true),
                ("contract.cached", UILayer.Popup, UICachePolicy.CachedSingleton, true),
            }) ui.Register(new UIDescriptor(new UIId(id), scene, layer, cache, UICoverPolicy.KeepVisible,
                modal ? UIInputPolicy.Modal : UIInputPolicy.Normal, UIFocusPolicy.GrabFirst));

            var chrome = new UIContractProbeState();
            var chromeHandle = await ui.OpenAsync(new UIId("contract.chrome"), chrome);
            foreach (var pageId in new[] { "contract.page_a", "contract.page_b" })
            {
                var page = new UIContractProbeState();
                var pageHandle = await ui.NavigateAsync(new UIId(pageId), page);
                page.Screen.MouseFilter = Control.MouseFilterEnum.Stop;
                await ClickAsync(host);
                Require(chrome.ClickCount > 0 && page.ClickCount == 0, "Full-screen page blocked persistent chrome.");
                var before = chrome.ClickCount;
                var first = new UIContractProbeState();
                _ = await ui.OpenAsync(new UIId("contract.popup"), first);
                first.Screen.GetNode<Button>("Hit").Position = new Vector2(150, 10);
                await ClickAsync(host);
                Require(chrome.ClickCount == before, "Modal popup allowed a click through to chrome.");
                Require(chrome.Screen.GetNode<Button>("Hit").GetFocusModeWithOverride() == Control.FocusModeEnum.None,
                    "Modal popup left chrome keyboard focus enabled.");
                var second = new UIContractProbeState();
                _ = await ui.OpenAsync(new UIId("contract.popup"), second);
                await ClickAsync(host);
                Require(second.ClickCount == 1 && first.ClickCount == 0, "Top popup did not receive pointer input.");
                Require(await ui.RequestBackAsync() && second.HideCount == 1 && first.HideCount == 0, "Back did not close newest popup.");
                Require(await ui.RequestBackAsync() && first.HideCount == 1, "Back did not close older popup.");
                await ClickAsync(host);
                Require(chrome.ClickCount == before + 1, "Closing modal stack did not restore chrome input.");
                Require(await ui.RequestBackAsync() && pageHandle.IsClosed && !chromeHandle.IsClosed, "Back removed chrome or skipped page.");
            }
            Require(!await ui.RequestBackAsync(), "Back must leave persistent chrome alone.");
            await chromeHandle.CloseAsync();
            GD.Print("LX_UI_CHROME_MODAL_PASS");

            foreach (var cached in new[] { false, true })
            foreach (var entering in new[] { false, true })
            foreach (var ownerCancel in new[] { false, true })
            {
                var owner = lifetime.CreateChild("RaceOwner");
                var id = new UIId(cached ? "contract.cached" : "contract.popup");
                var state = new UIContractProbeState { WaitInShow = !entering, WaitInEnter = entering };
                var opening = ui.OpenAsync(id, state, owner).AsTask();
                await (entering ? state.Entered.Task : state.Shown.Task);
                if (ownerCancel) await owner.DisposeAsync();
                else Require(await ui.RequestBackAsync(), "Back did not close an opening activation.");
                try { await opening; throw new InvalidOperationException("Closed opening unexpectedly succeeded."); }
                catch (OperationCanceledException) { }
                Require(state.Token.IsCancellationRequested && state.HideCount == 1 &&
                    state.EnterCount == (entering ? 1 : 0) && !ui.IsOpen(id), "Opening callback survived close.");
                var reopened = new UIContractProbeState();
                var handle = await ui.OpenAsync(id, reopened);
                Require(reopened.EnterCount == 1, "Cached immediate reopen did not complete.");
                if (cached) Require(ReferenceEquals(state.Screen, reopened.Screen), "Healthy cached screen was not reused.");
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(!handle.IsClosed && ui.IsOpen(id), "Deferred owner cancellation closed a newer activation.");
                await handle.CloseAsync();
                await owner.DisposeAsync();
            }
            foreach (var cached in new[] { false, true })
            foreach (var ownerFirst in new[] { false, true })
            {
                var owner = lifetime.CreateChild("ClosingOwner");
                var state = new UIContractProbeState { WaitInHide = true };
                var handle = await ui.OpenAsync(new UIId(cached ? "contract.cached" : "contract.popup"), state, owner);
                var firstClose = ownerFirst ? owner.DisposeAsync().AsTask() : handle.CloseAsync().AsTask();
                await state.Hiding.Task;
                var secondClose = ownerFirst ? handle.CloseAsync().AsTask() : owner.DisposeAsync().AsTask();
                var bothPending = !firstClose.IsCompleted && !secondClose.IsCompleted;
                state.HideReleased.SetResult();
                await Task.WhenAll(firstClose, secondClose);
                Require(bothPending, "Owner disposal did not join an in-progress UI close.");
                Require(state.HideCount == 1 && handle.IsClosed, "Concurrent owner/handle close did not coalesce.");
            }
            // A non-cooperative hook is quarantined until it returns; cancellation cannot stop arbitrary user code.
            var late = new UIContractProbeState { WaitInShow = true, IgnoreCancellation = true, FailOnRelease = true };
            var lateOpen = ui.OpenAsync(new UIId("contract.cached"), late).AsTask();
            await late.Shown.Task;
            var lateClose = ui.RequestBackAsync().AsTask();
            Require(late.Token.IsCancellationRequested && !lateClose.IsCompleted && GodotObject.IsInstanceValid(late.Screen),
                "Unfinished callback was recycled/freed or not cancelled.");
            late.Released.SetResult();
            try { await lateOpen; throw new InvalidOperationException("Real show failure was swallowed."); }
            catch (InvalidOperationException exception) when (exception.Message == "probe-show-failure") { }
            await lateClose;

            var stale = await ui.OpenAsync(new UIId("contract.cached"), new UIContractProbeState());
            await ui.RequestBackAsync();
            var fresh = await ui.OpenAsync(new UIId("contract.cached"), new UIContractProbeState());
            await stale.CloseAsync();
            Require(!fresh.IsClosed && ui.IsOpen(fresh.UIId), "Stale cached handle closed a newer activation.");
            await fresh.CloseAsync();

            var transient = new UIContractProbeState { WaitInShow = true };
            var singleton = new UIContractProbeState { WaitInEnter = true };
            var a = ui.OpenAsync(new UIId("contract.popup"), transient).AsTask();
            var b = ui.OpenAsync(new UIId("contract.cached"), singleton).AsTask();
            await transient.Shown.Task; await singleton.Entered.Task;
            await ui.DisposeAsync();
            foreach (var task in new[] { a, b })
            {
                try { await task; throw new InvalidOperationException("Service shutdown returned an open handle."); }
                catch (OperationCanceledException) { }
            }
            Require(ui.Snapshot().Count == 0, "Service shutdown retained UI activations.");
            GD.Print("LX_UI_OPEN_CLOSE_RACES_PASS");
        }
        finally
        {
            await ui.DisposeAsync();
            fixtureHost.QueueFree();
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Require(original.Res.Snapshot().Sum(item => item.LeaseCount) == baselineLeases, "UI contract fixtures leaked resource leases.");
    }

    private static async Task ClickAsync(Node host)
    {
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        var viewport = host.GetViewport();
        var position = new Vector2(32, 32);
        viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
        viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false }, true);
    }
    private static void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
    }
}
