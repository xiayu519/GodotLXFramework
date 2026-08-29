using LX.Core.Diagnostics;

namespace LXFramework.Core.Tests;

public sealed class MetricRegistryTests
{
    [Fact]
    public void Increment_RejectsCounterOverflow()
    {
        var metrics = new MetricRegistry();
        metrics.Increment("counter", long.MaxValue);

        Assert.Throws<OverflowException>(() => metrics.Increment("counter"));
    }

    [Fact]
    public void SetGauge_RejectsNonFiniteValues()
    {
        var metrics = new MetricRegistry();

        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.SetGauge("gauge", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.SetGauge("gauge", double.PositiveInfinity));
    }

    [Fact]
    public void Snapshot_IsIndependentFromLaterRegistryChanges()
    {
        var metrics = new MetricRegistry();
        metrics.SetGauge("gauge", 1);
        metrics.Increment("counter", 2);
        var snapshot = metrics.Snapshot();

        metrics.SetGauge("gauge", 3);
        metrics.Increment("counter", 4);

        Assert.Equal(1, snapshot.Gauges["gauge"]);
        Assert.Equal(2, snapshot.Counters["counter"]);
    }
}
