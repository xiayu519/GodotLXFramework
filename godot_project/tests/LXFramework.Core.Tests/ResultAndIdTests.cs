using LX.Core.Common;

namespace LXFramework.Core.Tests;

public sealed class ResultAndIdTests
{
    private sealed class ItemTag;

    [Fact]
    public void Failure_DoesNotExposeAValue()
    {
        var result = Result<int>.Failure("missing", "No value");

        Assert.True(result.IsFailure);
        Assert.Equal("missing", result.Error?.Code);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void StrongId_NormalizesSurroundingWhitespace()
    {
        var id = new StrongId<ItemTag>("  engine_v8  ");

        Assert.Equal("engine_v8", id.Value);
    }
}
