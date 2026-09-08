using Godot;
using LX.Core.Lifetime;
using LX.Generated;
using LX.Pooling;
using LX.Res;
using LX.Runtime;

namespace LX.Validation;

internal partial class VisualLifetimeProbe : ColorRect, ILXContextReceiver, IVisualCaptureReady
{
    [Export] public int WaitFrames { get; set; } = 180;
    private LXContext _context = null!;
    private LifetimeScope _lifetime = null!;
    public bool IsLXInitialized { get; private set; }
    internal static int CompletedCleanups { get; private set; }
    internal static int CompletedPoolReturns { get; private set; }
    internal static bool FailCleanup { get; set; }
    public void Initialize(LXContext context, LifetimeScope lifetime)
    {
        _context = context;
        _lifetime = lifetime;
        IsLXInitialized = true;
        Color = Colors.SteelBlue;
    }
    public async ValueTask WaitForVisualCaptureReadyAsync(CancellationToken cancellationToken = default)
    {
        _lifetime.Own(_context.Res.AcquireGenerated(
            "generated://validation/visual_cleanup/" + GetInstanceId(), () => new Gradient(), AssetCachePolicy.Transient));
        var pool = _lifetime.Own(new NodePool<Node>(() => new Node()));
        _lifetime.Defer(() =>
        {
            if (pool.RentedCount != 0) throw new InvalidOperationException("Visual cleanup retained a borrowed node.");
            CompletedPoolReturns++;
        });
        _ = pool.RentLease(this, _lifetime);
        // Deliberately do not own the returned handle: parentLifetime itself must await its UI closure.
        _ = await _context.UI.OpenAsync(UICatalog.FrameworkStatus.Id, parentLifetime: _lifetime, cancellationToken: cancellationToken);
        _lifetime.Own(new FrameCleanup(this));
        if (WaitFrames < 0)
        {
            GD.Print("LX_VISUAL_NEVER_READY_STARTED");
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        for (var frame = 0; frame < WaitFrames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print("LX_VISUAL_DELAYED_READY_PASS");
    }
    private sealed class FrameCleanup(VisualLifetimeProbe node) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
                throw new InvalidOperationException("Capture scene freed before asynchronous cleanup.");
            CompletedCleanups++;
            GD.Print("LX_VISUAL_ASYNC_CLEANUP_PASS");
            if (FailCleanup) throw new InvalidOperationException("expected-visual-cleanup-failure");
        }
    }
}
