using LX.Core.World;

namespace LXFramework.Core.Tests;

public sealed class ChunkPlannerTests
{
    [Fact]
    public void VisibleSquare_IsBoundedAndLoadsNearestFirst()
    {
        ChunkCoordinate[] available =
        [
            new(0, 0),
            new(1, 0),
            new(-1, 0),
            new(2, 0),
            new(1, 1),
        ];

        var visible = ChunkPlanner.VisibleSquare(new ChunkCoordinate(0, 0), 1, available);

        Assert.Equal(new ChunkCoordinate(0, 0), visible[0]);
        Assert.DoesNotContain(new ChunkCoordinate(2, 0), visible);
        Assert.Equal(4, visible.Count);
    }

    [Fact]
    public void VisibleSquare_PredicateRetainsVirtualCoordinatesForWrappingSources()
    {
        var visible = ChunkPlanner.VisibleSquare(
            new ChunkCoordinate(2, 7),
            1,
            _ => true);

        Assert.Equal(9, visible.Count);
        Assert.Contains(new ChunkCoordinate(2, 8), visible);
        Assert.Equal(new ChunkCoordinate(2, 7), visible[0]);
    }
}
