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
            if (!report.Success || VisualLifetimeProbe.CompletedCleanups != cleanups + round + 1 ||
                VisualLifetimeProbe.CompletedPoolReturns != poolReturns + round + 1 ||
                context.UI.Snapshot().Count != uiCount || context.Res.Snapshot().Sum(item => item.LeaseCount) != leases ||
                host.GetChildCount() != children)
                throw new InvalidOperationException("Repeated visual capture did not close UI, leases, pool borrows and scene instances.");
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
        if (VisualLifetimeProbe.CompletedPoolReturns != poolReturns + 4 || context.UI.Snapshot().Count != uiCount ||
            context.Res.Snapshot().Sum(item => item.LeaseCount) != leases || host.GetChildCount() != children)
            throw new InvalidOperationException("Failed visual cleanup leaked UI, leases or nodes.");
        GD.Print("LX_VISUAL_ORDERED_CLEANUP_PASS");
    }
}
