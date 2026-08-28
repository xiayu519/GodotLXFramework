namespace LX.Core.World;

public readonly record struct ChunkCoordinate(int X, int Y)
{
    public int ChebyshevDistanceTo(ChunkCoordinate other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public int ManhattanDistanceTo(ChunkCoordinate other) =>
        Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}

public static class ChunkPlanner
{
    public static IReadOnlyList<ChunkCoordinate> VisibleSquare(
        ChunkCoordinate focus,
        int radius,
        Func<ChunkCoordinate, bool> isAvailable)
    {
        if (radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }
        ArgumentNullException.ThrowIfNull(isAvailable);

        var result = new List<ChunkCoordinate>((radius * 2 + 1) * (radius * 2 + 1));
        for (var y = focus.Y - radius; y <= focus.Y + radius; y++)
        {
            for (var x = focus.X - radius; x <= focus.X + radius; x++)
            {
                var coordinate = new ChunkCoordinate(x, y);
                if (isAvailable(coordinate))
                {
                    result.Add(coordinate);
                }
            }
        }
        return result
            .OrderBy(coordinate => coordinate.ManhattanDistanceTo(focus))
            .ThenBy(coordinate => coordinate.Y)
            .ThenBy(coordinate => coordinate.X)
            .ToArray();
    }

    public static IReadOnlyList<ChunkCoordinate> VisibleSquare(
        ChunkCoordinate focus,
        int radius,
        IEnumerable<ChunkCoordinate> available)
    {
        if (radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        ArgumentNullException.ThrowIfNull(available);
        return available
            .Distinct()
            .Where(coordinate => coordinate.ChebyshevDistanceTo(focus) <= radius)
            .OrderBy(coordinate => coordinate.ManhattanDistanceTo(focus))
            .ThenBy(coordinate => coordinate.Y)
            .ThenBy(coordinate => coordinate.X)
            .ToArray();
    }
}
