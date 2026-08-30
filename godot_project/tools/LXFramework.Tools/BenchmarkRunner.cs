using System.Diagnostics;
using LX.Core.Diagnostics;
using LX.Core.Events;
using LX.Core.Pooling;

namespace LXFramework.Tools;

internal static class BenchmarkRunner
{
    private const int Iterations = 100_000;
    private const int SampleCount = 5;

    public static int Run(string root)
    {
        var gatesPath = Path.Combine(root, "tests", "Performance", "benchmark-gates.json");
        var gates = ToolFiles.ReadJson<BenchmarkGateDocument>(gatesPath);
        ValidateGates(gates);

        var cases = new[]
        {
            Measure("diagnostic_log.write", gates, () =>
            {
                var log = new DiagnosticLog(256);
                for (var index = 0; index < Iterations; index++)
                {
                    log.Write(DiagnosticSeverity.Debug, "benchmark", "message");
                }
            }),
            MeasureEventHub(gates),
            Measure("object_pool.rent_return", gates, () =>
            {
                using var pool = new ObjectPool<PooledBuffer>(() => new PooledBuffer(), maxRetained: 32);
                for (var index = 0; index < Iterations; index++)
                {
                    var item = pool.Rent();
                    item.Value = index;
                    pool.Return(item);
                }
            }),
            Measure("object_pool.rent_lease", gates, () =>
            {
                using var pool = new ObjectPool<PooledBuffer>(() => new PooledBuffer(), maxRetained: 32);
                for (var index = 0; index < Iterations; index++)
                {
                    using var item = pool.RentLease();
                    item.Value.Value = index;
                }
            }),
        };
        var failures = cases.SelectMany(item => item.Failures).ToArray();
        var report = new BenchmarkReport(
            "lx.benchmark-report",
            2,
            DateTimeOffset.UtcNow,
            Environment.Version.ToString(),
            Iterations,
            SampleCount,
            failures.Length == 0,
            cases,
            failures);
        ToolFiles.WriteJson(Path.Combine(root, ".lx", "benchmark.json"), report);
        foreach (var item in cases)
        {
            Console.WriteLine(
                $"{item.Name,-28} {item.OperationsPerSecond,12:N0} ops/s  " +
                $"{item.AllocatedBytesPerOperation,7:0.00} B/op  {(item.Success ? "PASS" : "FAIL")}");
        }
        Console.WriteLine("report                       .lx/benchmark.json");
        if (failures.Length == 0)
        {
            return 0;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"benchmark gate failed: {failure}");
        }
        return 1;
    }

    private static BenchmarkCase MeasureEventHub(BenchmarkGateDocument gates)
    {
        using var events = new EventHub();
        using var subscription = events.Subscribe<int>(static _ => { });
        return Measure("event_hub.publish", gates, () =>
        {
            for (var index = 0; index < Iterations; index++)
            {
                events.Publish(index);
            }
        });
    }

    private static BenchmarkCase Measure(
        string name,
        BenchmarkGateDocument document,
        Action action)
    {
        var gate = document.Cases.Single(item => item.Name == name);
        action();
        var samples = new List<BenchmarkSample>(SampleCount);
        for (var sampleIndex = 0; sampleIndex < SampleCount; sampleIndex++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = Stopwatch.GetTimestamp();
            action();
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            samples.Add(new BenchmarkSample(
                elapsed.TotalMilliseconds,
                Iterations / elapsed.TotalSeconds,
                allocated,
                allocated / (double)Iterations));
        }

        var duration = Median(samples.Select(item => item.DurationMilliseconds));
        var operationsPerSecond = Median(samples.Select(item => item.OperationsPerSecond));
        var allocatedBytes = (long)Median(samples.Select(item => (double)item.AllocatedBytes));
        var allocatedBytesPerOperation = Median(samples.Select(item => item.AllocatedBytesPerOperation));
        var minimumOperationsPerSecond =
            gate.ReferenceOperationsPerSecond * (1.0 - (gate.MaxRegressionPercent / 100.0));
        var failures = new List<string>();
        if (operationsPerSecond < minimumOperationsPerSecond)
        {
            failures.Add(
                $"{name} throughput {operationsPerSecond:N0} ops/s < {minimumOperationsPerSecond:N0} ops/s " +
                $"(reference {gate.ReferenceOperationsPerSecond:N0}, allowed regression {gate.MaxRegressionPercent:0.#}%)");
        }
        if (allocatedBytesPerOperation > gate.MaxAllocatedBytesPerOperation)
        {
            failures.Add(
                $"{name} allocation {allocatedBytesPerOperation:0.00} B/op > " +
                $"{gate.MaxAllocatedBytesPerOperation:0.00} B/op");
        }

        return new BenchmarkCase(
            name,
            duration,
            operationsPerSecond,
            allocatedBytes,
            allocatedBytesPerOperation,
            gate.ReferenceOperationsPerSecond,
            minimumOperationsPerSecond,
            gate.MaxAllocatedBytesPerOperation,
            ((gate.ReferenceOperationsPerSecond - operationsPerSecond) / gate.ReferenceOperationsPerSecond) * 100.0,
            failures.Count == 0,
            failures,
            samples);
    }

    private static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        return values[values.Length / 2];
    }

    private static void ValidateGates(BenchmarkGateDocument gates)
    {
        if (gates.Schema != "lx.benchmark-gates" || gates.SchemaVersion != 1 ||
            gates.Iterations != Iterations || gates.Samples != SampleCount)
        {
            throw new InvalidDataException(
                $"Performance gate file must be lx.benchmark-gates/v1 with {Iterations} iterations and {SampleCount} samples.");
        }

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "diagnostic_log.write",
            "event_hub.publish",
            "object_pool.rent_return",
            "object_pool.rent_lease",
        };
        if (gates.Cases.Count != expected.Count || gates.Cases.Any(item =>
                !expected.Remove(item.Name) ||
                !double.IsFinite(item.ReferenceOperationsPerSecond) || item.ReferenceOperationsPerSecond <= 0 ||
                !double.IsFinite(item.MaxRegressionPercent) || item.MaxRegressionPercent is < 0 or >= 100 ||
                !double.IsFinite(item.MaxAllocatedBytesPerOperation) || item.MaxAllocatedBytesPerOperation < 0))
        {
            throw new InvalidDataException("Performance gate cases are missing, duplicated, unknown, or invalid.");
        }
    }

    private sealed class PooledBuffer
    {
        public int Value { get; set; }
    }
}

internal sealed record BenchmarkGateDocument(
    string Schema,
    int SchemaVersion,
    int Iterations,
    int Samples,
    IReadOnlyList<BenchmarkGate> Cases);

internal sealed record BenchmarkGate(
    string Name,
    double ReferenceOperationsPerSecond,
    double MaxRegressionPercent,
    double MaxAllocatedBytesPerOperation);

internal sealed record BenchmarkSample(
    double DurationMilliseconds,
    double OperationsPerSecond,
    long AllocatedBytes,
    double AllocatedBytesPerOperation);

internal sealed record BenchmarkCase(
    string Name,
    double DurationMilliseconds,
    double OperationsPerSecond,
    long AllocatedBytes,
    double AllocatedBytesPerOperation,
    double ReferenceOperationsPerSecond,
    double MinimumOperationsPerSecond,
    double MaxAllocatedBytesPerOperation,
    double RegressionPercent,
    bool Success,
    IReadOnlyList<string> Failures,
    IReadOnlyList<BenchmarkSample> Samples);

internal sealed record BenchmarkReport(
    string Schema,
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    string DotnetVersion,
    int Iterations,
    int SampleCount,
    bool Success,
    IReadOnlyList<BenchmarkCase> Cases,
    IReadOnlyList<string> Failures);
