using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace LXFramework.Tools;

internal static class RuntimeBridgeClient
{
    private static readonly HashSet<string> Sections = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "runtime", "events", "scheduler", "actions", "metrics", "resources",
        "ui", "features", "audio", "input", "localization", "settings", "logs", "performance",
    };

    private static readonly string[] TrackedMetrics =
    [
        "assets.leases", "assets.inflight", "ui.active", "features.active",
        "audio.operations", "world.chunks_active", "lifetime.root_owned",
    ];

    public static int Run(string root, IReadOnlyList<string> arguments)
    {
        var operation = arguments.Count == 0 ? "status" : arguments[0].ToLowerInvariant();
        return operation switch
        {
            "status" when arguments.Count == 1 || arguments.Count == 0 => Status(root),
            "snapshot" => Snapshot(root, arguments.Skip(1).ToArray()),
            "sample" => Sample(root, arguments.Skip(1).ToArray()),
            _ => Usage(),
        };
    }

    private static int Status(string root)
    {
        if (!TryReadLiveSession(root, out var session, out var error))
        {
            Console.Error.WriteLine($"runtime: {error}");
            return 1;
        }

        var output = Path.Combine(root, ".lx", "runtime", "status.json");
        ToolFiles.WriteJson(output, session);
        Console.WriteLine(
            $"runtime active: session={session.SessionId}, generation={session.Generation}, " +
            $"pid={session.ProcessId} -> {ToolFiles.Relative(root, output)}");
        return 0;
    }

    private static int Snapshot(string root, IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 1)
        {
            return Usage();
        }

        var section = arguments.Count == 0 ? "all" : arguments[0].ToLowerInvariant();
        if (!Sections.Contains(section))
        {
            Console.Error.WriteLine(
                $"runtime: unknown snapshot section '{section}'. Available: {string.Join(", ", Sections)}");
            return 2;
        }
        if (!TryReadLiveSession(root, out var session, out var error))
        {
            Console.Error.WriteLine($"runtime: {error}");
            return 1;
        }
        if (!TryRequest(root, session, section, null, out var response, out error))
        {
            Console.Error.WriteLine($"runtime: {error}");
            return 1;
        }

        var output = Path.Combine(root, ".lx", "runtime", $"snapshot-{section}.json");
        ToolFiles.WriteJson(output, response);
        Console.WriteLine(
            $"runtime snapshot '{section}' captured for session {session.SessionId} -> " +
            ToolFiles.Relative(root, output));
        return 0;
    }

    private static int Sample(string root, IReadOnlyList<string> arguments)
    {
        if (!TryParseSampleOptions(arguments, out var options, out var error))
        {
            Console.Error.WriteLine($"runtime: {error}");
            return 2;
        }
        if (!TryReadLiveSession(root, out var session, out error))
        {
            Console.Error.WriteLine($"runtime: {error}");
            return 1;
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var elapsed = Stopwatch.StartNew();
        var points = new List<RuntimePerformancePoint>();
        RuntimePerformancePayload? latest = null;
        while (true)
        {
            if (!TryRequest(root, session, "performance", options.DurationSeconds, out var response, out error) ||
                !TryReadPerformance(response, out latest, out error))
            {
                Console.Error.WriteLine($"runtime: {error}");
                return 1;
            }
            points.Add(ToPoint(response.CapturedAtUtc, latest));

            var remaining = TimeSpan.FromSeconds(options.DurationSeconds) - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }
            Thread.Sleep((int)Math.Min(options.IntervalMilliseconds, Math.Ceiling(remaining.TotalMilliseconds)));
        }

        var first = points[0];
        var last = points[^1];
        var heapDelta = last.ManagedHeapBytes - first.ManagedHeapBytes;
        var workingSetDelta = last.WorkingSetBytes - first.WorkingSetBytes;
        var metricDeltas = TrackedMetrics.ToDictionary(
            name => name,
            name => last.Metrics.GetValueOrDefault(name) - first.Metrics.GetValueOrDefault(name),
            StringComparer.Ordinal);
        var failures = Evaluate(options, latest!, heapDelta);
        var report = new RuntimePerformanceReport(
            "lx.runtime-performance-report",
            1,
            session.SessionId,
            session.Generation,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            options.DurationSeconds,
            elapsed.Elapsed.TotalSeconds,
            options.IntervalMilliseconds,
            latest!.Frames,
            latest.PhysicsFrames,
            heapDelta,
            workingSetDelta,
            metricDeltas,
            points,
            options.Budgets,
            failures.Count == 0,
            failures);
        var output = Path.Combine(
            root,
            ".lx",
            "runtime",
            "performance",
            $"sample-{startedAtUtc:yyyyMMdd-HHmmss}.json");
        ToolFiles.WriteJson(output, report);

        var state = failures.Count == 0 ? "passed" : "failed";
        Console.WriteLine(
            $"runtime performance {state}: {elapsed.Elapsed.TotalSeconds:0.0}s, " +
            $"frames={latest.Frames.SampleCount}, p95={latest.Frames.DeltaMilliseconds.P95:0.00}ms, " +
            $"p99={latest.Frames.DeltaMilliseconds.P99:0.00}ms, " +
            $"max={latest.Frames.DeltaMilliseconds.Maximum:0.00}ms, " +
            $"host-p95={latest.Frames.HostWorkMilliseconds.P95:0.00}ms, " +
            $"heapΔ={BytesToMebibytes(heapDelta):+0.00;-0.00;0.00}MiB -> {ToolFiles.Relative(root, output)}");
        if (failures.Count > 0)
        {
            Console.Error.WriteLine("runtime performance gates: " + string.Join("; ", failures));
            return 1;
        }
        return 0;
    }

    private static bool TryRequest(
        string root,
        RuntimeSession session,
        string section,
        double? windowSeconds,
        out RuntimeResponse response,
        out string error)
    {
        var runtimeRoot = Path.Combine(root, ".lx", "runtime");
        var requestPath = Path.Combine(runtimeRoot, "request.json");
        var responsePath = Path.Combine(runtimeRoot, "response.json");
        var requestId = Guid.NewGuid().ToString("N");
        ToolFiles.WriteJson(
            requestPath,
            new RuntimeRequest(
                "lx.runtime-request",
                1,
                requestId,
                session.SessionId,
                session.Generation,
                section,
                windowSeconds));

        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(50);
            if (!File.Exists(responsePath))
            {
                continue;
            }

            RuntimeResponse? candidate;
            try
            {
                candidate = ToolFiles.ReadJson<RuntimeResponse>(responsePath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (JsonException)
            {
                continue;
            }

            if (candidate.RequestId != requestId)
            {
                continue;
            }
            if (candidate.SessionId != session.SessionId || candidate.Generation != session.Generation)
            {
                response = null!;
                error = "response belongs to a stale runtime generation.";
                return false;
            }
            if (!candidate.Success)
            {
                response = null!;
                error = candidate.Error ?? $"snapshot '{section}' failed.";
                return false;
            }

            response = candidate;
            error = string.Empty;
            return true;
        }

        response = null!;
        error = "timed out waiting for the current Godot runtime.";
        return false;
    }

    private static bool TryReadPerformance(
        RuntimeResponse response,
        out RuntimePerformancePayload payload,
        out string error)
    {
        try
        {
            payload = response.Payload?.Deserialize<RuntimePerformancePayload>(ToolFiles.JsonOptions) ??
                      throw new InvalidDataException("performance response had no payload.");
            if (payload.Frames.SampleCount == 0)
            {
                throw new InvalidDataException("performance response had no frame samples.");
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            payload = null!;
            error = $"invalid performance response: {exception.Message}";
            return false;
        }
    }

    private static RuntimePerformancePoint ToPoint(
        DateTimeOffset capturedAtUtc,
        RuntimePerformancePayload payload)
    {
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var name in TrackedMetrics)
        {
            if (payload.Metrics.Gauges.TryGetValue(name, out var gauge))
            {
                metrics[name] = gauge;
            }
            else if (payload.Metrics.Counters.TryGetValue(name, out var counter))
            {
                metrics[name] = counter;
            }
            else
            {
                metrics[name] = 0;
            }
        }

        return new RuntimePerformancePoint(
            capturedAtUtc,
            payload.Memory.TotalAllocatedBytes,
            payload.Memory.ManagedHeapBytes,
            payload.Memory.WorkingSetBytes,
            payload.Memory.Gen0Collections,
            payload.Memory.Gen1Collections,
            payload.Memory.Gen2Collections,
            metrics);
    }

    private static IReadOnlyList<string> Evaluate(
        RuntimeSampleOptions options,
        RuntimePerformancePayload payload,
        long heapDelta)
    {
        var failures = new List<string>();
        AddMaximumFailure(failures, "frame p95", payload.Frames.DeltaMilliseconds.P95, options.Budgets.MaxP95Milliseconds, "ms");
        AddMaximumFailure(failures, "frame p99", payload.Frames.DeltaMilliseconds.P99, options.Budgets.MaxP99Milliseconds, "ms");
        AddMaximumFailure(failures, "frame max", payload.Frames.DeltaMilliseconds.Maximum, options.Budgets.MaxFrameMilliseconds, "ms");
        AddMaximumFailure(failures, "heap growth", BytesToMebibytes(heapDelta), options.Budgets.MaxHeapGrowthMebibytes, "MiB");
        return failures;
    }

    private static void AddMaximumFailure(
        ICollection<string> failures,
        string name,
        double actual,
        double? maximum,
        string unit)
    {
        if (maximum is not null && actual > maximum.Value)
        {
            failures.Add($"{name} {actual:0.00}{unit} > {maximum.Value:0.00}{unit}");
        }
    }

    private static bool TryParseSampleOptions(
        IReadOnlyList<string> arguments,
        out RuntimeSampleOptions options,
        out string error)
    {
        var duration = 15.0;
        var interval = 500;
        double? maxP95 = null;
        double? maxP99 = null;
        double? maxFrame = null;
        double? maxHeap = null;
        if (arguments.Count == 0 || !string.Equals(arguments[0], "performance", StringComparison.OrdinalIgnoreCase))
        {
            options = null!;
            error = "sample usage: lx runtime sample performance [--duration <1..60>] [--interval <250..5000>] [performance budgets]";
            return false;
        }

        for (var index = 1; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count ||
                !double.TryParse(arguments[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                options = null!;
                error = $"option '{arguments[index]}' requires a numeric value.";
                return false;
            }

            switch (arguments[index])
            {
                case "--duration":
                    duration = value;
                    break;
                case "--interval":
                    if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue ||
                        Math.Abs(value - Math.Truncate(value)) > double.Epsilon)
                    {
                        options = null!;
                        error = "--interval must be a whole number of milliseconds.";
                        return false;
                    }
                    interval = (int)value;
                    break;
                case "--max-p95-ms":
                    maxP95 = value;
                    break;
                case "--max-p99-ms":
                    maxP99 = value;
                    break;
                case "--max-frame-ms":
                    maxFrame = value;
                    break;
                case "--max-heap-growth-mb":
                    maxHeap = value;
                    break;
                default:
                    options = null!;
                    error = $"unknown performance sample option '{arguments[index]}'.";
                    return false;
            }
        }

        if (!double.IsFinite(duration) || duration < 1 || duration > 60 || interval < 250 || interval > 5_000 ||
            new[] { maxP95, maxP99, maxFrame, maxHeap }
                .Any(value => value is not null && (!double.IsFinite(value.Value) || value.Value <= 0)))
        {
            options = null!;
            error = "duration must be 1..60 seconds, interval 250..5000 ms, and budgets finite positive numbers.";
            return false;
        }

        var budgets = new RuntimePerformanceBudgets(maxP95, maxP99, maxFrame, maxHeap);
        options = new RuntimeSampleOptions(duration, interval, budgets);
        error = string.Empty;
        return true;
    }

    private static bool TryReadLiveSession(
        string root,
        out RuntimeSession session,
        out string error)
    {
        var path = Path.Combine(root, ".lx", "runtime", "session.json");
        if (!File.Exists(path))
        {
            session = null!;
            error = "no runtime session was published; run the project with the Godot editor/debug binary.";
            return false;
        }

        try
        {
            session = ToolFiles.ReadJson<RuntimeSession>(path);
            using var process = Process.GetProcessById(session.ProcessId);
            if (process.HasExited ||
                session.State != "running" ||
                DateTimeOffset.UtcNow - session.HeartbeatAtUtc > TimeSpan.FromSeconds(4))
            {
                error = "the published runtime session is stale or stopped.";
                return false;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            session = null!;
            error = $"runtime session is unavailable: {exception.Message}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static double BytesToMebibytes(long bytes) => bytes / (1024.0 * 1024.0);

    private static int Usage()
    {
        Console.Error.WriteLine(
            "runtime usage: lx runtime status | snapshot [section] | sample performance " +
            "[--duration seconds] [--interval milliseconds] [--max-p95-ms value] " +
            "[--max-p99-ms value] [--max-frame-ms value] [--max-heap-growth-mb value]");
        return 2;
    }
}

internal sealed record RuntimeSession(
    string Schema,
    int SchemaVersion,
    string SessionId,
    long Generation,
    int ProcessId,
    DateTimeOffset HeartbeatAtUtc,
    string State,
    IReadOnlyList<string> Sections);

internal sealed record RuntimeRequest(
    string Schema,
    int SchemaVersion,
    string RequestId,
    string SessionId,
    long Generation,
    string Section,
    double? WindowSeconds = null);

internal sealed record RuntimeResponse(
    string Schema,
    int SchemaVersion,
    string RequestId,
    string SessionId,
    long Generation,
    DateTimeOffset CapturedAtUtc,
    bool Success,
    string? Error,
    string Section,
    JsonElement? Payload);

internal sealed record RuntimeSampleOptions(
    double DurationSeconds,
    int IntervalMilliseconds,
    RuntimePerformanceBudgets Budgets);

internal sealed record RuntimePerformanceBudgets(
    double? MaxP95Milliseconds,
    double? MaxP99Milliseconds,
    double? MaxFrameMilliseconds,
    double? MaxHeapGrowthMebibytes);

internal sealed record RuntimePerformancePayload(
    double WindowSeconds,
    RuntimeFrameStatisticsPayload Frames,
    RuntimeFrameStatisticsPayload PhysicsFrames,
    RuntimeMemoryPayload Memory,
    RuntimeMetricPayload Metrics);

internal sealed record RuntimeFrameStatisticsPayload(
    int SampleCount,
    double ObservedSeconds,
    RuntimeDistributionPayload DeltaMilliseconds,
    RuntimeDistributionPayload HostWorkMilliseconds,
    int Over16Milliseconds,
    int Over33Milliseconds,
    int Over50Milliseconds);

internal sealed record RuntimeDistributionPayload(
    double Average,
    double P50,
    double P95,
    double P99,
    double Minimum,
    double Maximum);

internal sealed record RuntimeMemoryPayload(
    long TotalAllocatedBytes,
    long ManagedHeapBytes,
    long WorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

internal sealed record RuntimeMetricPayload(
    IReadOnlyDictionary<string, double> Gauges,
    IReadOnlyDictionary<string, long> Counters);

internal sealed record RuntimePerformancePoint(
    DateTimeOffset CapturedAtUtc,
    long TotalAllocatedBytes,
    long ManagedHeapBytes,
    long WorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    IReadOnlyDictionary<string, double> Metrics);

internal sealed record RuntimePerformanceReport(
    string Schema,
    int SchemaVersion,
    string SessionId,
    long Generation,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    double RequestedDurationSeconds,
    double ActualDurationSeconds,
    int IntervalMilliseconds,
    RuntimeFrameStatisticsPayload Frames,
    RuntimeFrameStatisticsPayload PhysicsFrames,
    long ManagedHeapDeltaBytes,
    long WorkingSetDeltaBytes,
    IReadOnlyDictionary<string, double> MetricDeltas,
    IReadOnlyList<RuntimePerformancePoint> Samples,
    RuntimePerformanceBudgets Budgets,
    bool Success,
    IReadOnlyList<string> Failures);
