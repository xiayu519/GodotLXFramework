using LX.Core.Diagnostics;

namespace LXFramework.Core.Tests;

public sealed class DiagnosticLogTests
{
    [Fact]
    public void SnapshotKeepsNewestEntriesWithinCapacity()
    {
        var log = new DiagnosticLog(2);

        log.Write(DiagnosticSeverity.Debug, "test", "first");
        log.Write(DiagnosticSeverity.Information, "test", "second");
        log.Write(DiagnosticSeverity.Error, "test", "third");

        var entries = log.Snapshot();
        Assert.Equal(2, entries.Count);
        Assert.Equal("second", entries[0].Message);
        Assert.Equal("third", entries[1].Message);
        Assert.Single(log.Snapshot(DiagnosticSeverity.Error));
    }
}
