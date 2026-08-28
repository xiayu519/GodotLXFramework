using LX.Core.Lifetime;

namespace LXFramework.Core.Tests;

public sealed class LifetimeScopeTests
{
    [Fact]
    public void Dispose_CancelsFirstAndCleansInReverseOrder()
    {
        var scope = new LifetimeScope("test");
        var calls = new List<string>();
        scope.Defer(() => calls.Add(scope.Token.IsCancellationRequested ? "first-cancelled" : "first-live"));
        scope.Defer(() => calls.Add("second"));

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(["second", "first-cancelled"], calls);
        Assert.True(scope.IsDisposed);
    }

    [Fact]
    public void CreateChild_IsOwnedByParent()
    {
        var parent = new LifetimeScope("parent");
        var child = parent.CreateChild("child");

        parent.Dispose();

        Assert.True(child.IsDisposed);
        Assert.Equal("parent/child", child.Name);
    }

    [Fact]
    public void OwnAfterDispose_DisposesIncomingValueAndThrows()
    {
        var scope = new LifetimeScope("closed");
        scope.Dispose();
        var disposable = new ProbeDisposable();

        Assert.Throws<ObjectDisposedException>(() => scope.Own(disposable));
        Assert.True(disposable.Disposed);
    }

    private sealed class ProbeDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task DisposedChild_DetachesFromLongLivedParent()
    {
        await using var parent = new LifetimeScope("root");
        var child = parent.CreateChild("temporary-ui");

        Assert.Equal(1, parent.OwnedCount);

        await child.DisposeAsync();

        Assert.Equal(0, parent.OwnedCount);
    }

    [Fact]
    public void Dispose_StillCleansOwnedValuesWhenCancellationCallbackThrows()
    {
        var scope = new LifetimeScope("faulty-cancellation");
        var disposable = scope.Own(new ProbeDisposable());
        using var registration = scope.Token.Register(() => throw new InvalidOperationException("callback"));

        Assert.Throws<AggregateException>(() => scope.Dispose());

        Assert.True(disposable.Disposed);
    }

    [Fact]
    public void Token_RemainsObservableAfterScopeDisposal()
    {
        var scope = new LifetimeScope("token-cache");

        scope.Dispose();

        Assert.True(scope.Token.IsCancellationRequested);
    }
}
