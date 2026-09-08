using Godot;
using LX.Runtime;

namespace LX.Validation;

internal static class VisualContractSmoke
{
    internal static async Task RunAsync(Node host, LXContext context, CancellationToken cancellationToken)
    {
        var runner = new VisualCaptureRunner(host, context);
        var cleanups = VisualLifetimeProbe.CompletedCleanups;
        var poolReturns = VisualLifetimeProbe.CompletedPoolReturns;
        var leases = context.Res.Snapshot().Sum(item => item.LeaseCount);
        var uiCount = context.UI.Snapshot().Count;
        var children = host.GetChildCount();
        var output = ProjectSettings.GlobalizePath("res://.lx/visual/lifetime-smoke.png");
        for (var round = 0; round < 3; round++)
        {
            var report = await runner.RunAsync("capture", "SemanticControl", "lifetime-smoke",
                "res://scene/validation/delayed_capture_probe.tscn", new Vector2I(320, 180), 1, 0, 0,
                null, output, null, null, cancellationToken);
            RequireClosed($"capture round {round + 1}", report.Success, round + 1);
        }
        VisualLifetimeProbe.FailCleanup = true;
        try
        {
            await runner.RunAsync("capture", "SemanticControl", "cleanup-failure",
                "res://scene/validation/delayed_capture_probe.tscn", new Vector2I(320, 180), 1, 0, 0,
                null, output, null, null, cancellationToken);
            throw new InvalidOperationException("Visual cleanup failure was reported as success.");
        }
        catch (AggregateException exception) when (exception.ToString().Contains("expected-visual-cleanup-failure", StringComparison.Ordinal)) { }
        finally { VisualLifetimeProbe.FailCleanup = false; }
        RequireClosed("expected cleanup failure", true, 4);
        GD.Print("LX_VISUAL_ORDERED_CLEANUP_PASS");

        void RequireClosed(string phase, bool captureSuccess, int completedRounds)
        {
            var actualCleanups = VisualLifetimeProbe.CompletedCleanups;
            var actualReturns = VisualLifetimeProbe.CompletedPoolReturns;
            var actualUi = context.UI.Snapshot().Count;
            var actualLeases = context.Res.Snapshot().Sum(item => item.LeaseCount);
            var actualChildren = host.GetChildCount();
            if (!captureSuccess || actualCleanups != cleanups + completedRounds ||
                actualReturns != poolReturns + completedRounds || actualUi != uiCount ||
                actualLeases != leases || actualChildren != children)
                throw new InvalidOperationException(
                    $"Visual cleanup mismatch ({phase}; actual/expected): success={captureSuccess}/True, " +
                    $"cleanups={actualCleanups}/{cleanups + completedRounds}, " +
                    $"poolReturns={actualReturns}/{poolReturns + completedRounds}, UI={actualUi}/{uiCount}, " +
                    $"leases={actualLeases}/{leases}, hostChildren={actualChildren}/{children}.");
        }
    }
}
