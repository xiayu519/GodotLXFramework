using System.Diagnostics;
using LX.Core.Diagnostics;
using LX.Core.Events;
using LX.Core.Pooling;

namespace LXFramework.Tools;

internal static class BenchmarkRunner
{
    private const int Iterations = 100_000;

    public static int Run(string root)
    {
        var eventHubPublish = MeasureEventHubPublish();
        var cases = new[]
        {
            Measure("diagnostic_log.write", () =>
            {
                var log = new DiagnosticLog(256);
                for (var index = 0; index < Iterations; index++)
                {
                    log.Write(DiagnosticSeverity.Debug, "benchmark", "message");
                }
            }),
            eventHubPublish,
            Measure("object_pool.rent_return", () =>
            {
                using var pool = new ObjectPool<PooledBuffer>(() => new PooledBuffer(), maxRetained: 32);
                for (var index = 0; index < Iterations; index++)
                {
                    using var item = pool.RentLease();
                    item.Value.Value = index;
                }
            }),
        };
        var report = new BenchmarkReport(
            "lx.benchmark-report",
            1,
            DateTimeOffset.UtcNow,
            Environment.Version.ToString(),
            Iterations,
            cases);
        ToolFiles.WriteJson(Path.Combine(root, ".lx", "benchmark.json"), report);
        foreach (var item in cases)
        {
            Console.WriteLine($"{item.Name,-28} {item.OperationsPerSecond,12:N0} ops/s  {item.AllocatedBytes,12:N0} bytes");
        }
        Console.WriteLine("report                       .lx/benchmark.json");
        if (eventHubPublish.AllocatedBytes != 0)
        {
            Console.Error.WriteLine(
                $"event_hub.publish allocation gate failed: expected 0, actual {eventHubPublish.AllocatedBytes} bytes.");
            return 1;
        }
        return 0;
    }

    private static BenchmarkCase MeasureEventHubPublish()
    {
        using var events = new EventHub();
        using var subscription = events.Subscribe<int>(static _ => { });
        for (var index = 0; index < 128; index++)
        {
            events.Publish(index);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        for (var index = 0; index < Iterations; index++)
        {
            events.Publish(index);
        }
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new BenchmarkCase(
            "event_hub.publish",
            elapsed.TotalMilliseconds,
            Iterations / elapsed.TotalSeconds,
            allocated);
    }

    private static BenchmarkCase Measure(string name, Action action)
    {
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new BenchmarkCase(
            name,
            stopwatch.Elapsed.TotalMilliseconds,
            Iterations / stopwatch.Elapsed.TotalSeconds,
            allocated);
    }

    private sealed class PooledBuffer
    {
        public int Value { get; set; }
    }
}

internal sealed record BenchmarkCase(
    string Name,
    double DurationMilliseconds,
    double OperationsPerSecond,
    long AllocatedBytes);

internal sealed record BenchmarkReport(
    string Schema,
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    string DotnetVersion,
    int Iterations,
    IReadOnlyList<BenchmarkCase> Cases);
