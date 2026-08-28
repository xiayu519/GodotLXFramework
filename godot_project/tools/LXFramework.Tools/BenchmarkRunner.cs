using System.Diagnostics;
using LX.Core.Diagnostics;
using LX.Core.Events;
using LX.Core.Lifetime;
using LX.Core.Pooling;

namespace LXFramework.Tools;

internal static class BenchmarkRunner
{
    private const int Iterations = 100_000;

    public static int Run(string root)
    {
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
            Measure("event_hub.publish", () =>
            {
                using var lifetime = new LifetimeScope("benchmark");
                using var events = new EventHub();
                events.Subscribe<int>(_ => { }, lifetime);
                for (var index = 0; index < Iterations; index++)
                {
                    events.Publish(index);
                }
            }),
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
        return 0;
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
