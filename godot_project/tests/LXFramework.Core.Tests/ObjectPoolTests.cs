using LX.Core.Pooling;

namespace LXFramework.Core.Tests;

public sealed class ObjectPoolTests
{
    [Fact]
    public void Lease_ReturnsObjectExactlyOnce()
    {
        var created = 0;
        using var pool = new ObjectPool<List<int>>(
            () =>
            {
                created++;
                return [];
            },
            list => list.Clear());

        var lease = pool.RentLease();
        lease.Value.Add(7);
        lease.Dispose();
        lease.Dispose();
        var reused = pool.Rent();

        Assert.Equal(1, created);
        Assert.Empty(reused);
    }

    [Fact]
    public void Return_RejectsDuplicateObject()
    {
        using var pool = new ObjectPool<object>(() => new object());
        var item = pool.Rent();

        pool.Return(item);

        Assert.Throws<InvalidOperationException>(() => pool.Return(item));
    }

    [Fact]
    public void ReturnAfterDispose_DiscardsOutstandingObject()
    {
        var discarded = 0;
        var pool = new ObjectPool<object>(() => new object(), discard: _ => discarded++);
        var item = pool.Rent();

        pool.Dispose();
        pool.Return(item);

        Assert.Equal(1, discarded);
        Assert.Throws<ObjectDisposedException>(() => pool.Rent());
    }

    [Fact]
    public void Rent_RejectsNullFactoryResult()
    {
        using var pool = new ObjectPool<object>(() => null!);

        Assert.Throws<InvalidOperationException>(() => pool.Rent());
    }

    [Fact]
    public void Return_RejectsObjectFromAnotherPool()
    {
        using var pool = new ObjectPool<object>(() => new object());

        Assert.Throws<InvalidOperationException>(() => pool.Return(new object()));
        Assert.Equal(0, pool.RetainedCount);
        Assert.Equal(0, pool.RentedCount);
    }

    [Fact]
    public void Counts_DistinguishRentedAndRetainedObjects()
    {
        using var pool = new ObjectPool<object>(() => new object());

        var item = pool.Rent();
        Assert.Equal(1, pool.RentedCount);
        Assert.Equal(0, pool.RetainedCount);

        pool.Return(item);
        Assert.Equal(0, pool.RentedCount);
        Assert.Equal(1, pool.RetainedCount);
        Assert.Equal(new PoolStatistics(1, 0, 0, 0, 1), pool.Statistics);

        var reused = pool.Rent();
        Assert.Equal(1, pool.Statistics.Reused);
        pool.Return(reused);
    }
}
