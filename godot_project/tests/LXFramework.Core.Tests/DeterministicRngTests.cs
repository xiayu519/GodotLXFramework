using LX.Core.Random;

namespace LXFramework.Core.Tests;

public sealed class DeterministicRngTests
{
    [Fact]
    public void CaptureAndRestore_ReplaysTheSameSequence()
    {
        var random = new DeterministicRng(12345);
        _ = random.NextUInt();
        var state = random.Capture();
        var expected = Enumerable.Range(0, 16).Select(_ => random.NextUInt()).ToArray();

        random.Restore(state);
        var actual = Enumerable.Range(0, 16).Select(_ => random.NextUInt()).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NextInt_StaysInsideRequestedRange()
    {
        var random = new DeterministicRng(9876);

        for (var index = 0; index < 10_000; index++)
        {
            var value = random.NextInt(-4, 9);
            Assert.InRange(value, -4, 8);
        }
    }
}
