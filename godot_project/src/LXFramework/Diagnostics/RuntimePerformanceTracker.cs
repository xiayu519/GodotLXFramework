using System.Diagnostics;

namespace LX.Diagnostics;

internal sealed class RuntimePerformanceTracker
{
    private const int Capacity = 16_384;
    private const double DefaultWindowSeconds = 15.0;
    private const double MaximumWindowSeconds = 60.0;

    private readonly SampleRing _frames = new(Capacity);
    private readonly SampleRing _physicsFrames = new(Capacity);

    public void RecordFrame(double deltaMilliseconds, double hostWorkMilliseconds) =>
        _frames.Add(deltaMilliseconds, hostWorkMilliseconds);

    public void RecordPhysicsFrame(double deltaMilliseconds, double hostWorkMilliseconds) =>
        _physicsFrames.Add(deltaMilliseconds, hostWorkMilliseconds);

    public RuntimePerformanceRecord Snapshot(double? requestedWindowSeconds, object metrics)
    {
        var windowSeconds = requestedWindowSeconds ?? DefaultWindowSeconds;
        if (!double.IsFinite(windowSeconds) || windowSeconds < 1.0 || windowSeconds > MaximumWindowSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedWindowSeconds),
                $"Performance window must be between 1 and {MaximumWindowSeconds:0} seconds.");
        }

        var now = Stopwatch.GetTimestamp();
        return new RuntimePerformanceRecord(
            windowSeconds,
            _frames.Snapshot(now, windowSeconds),
            _physicsFrames.Snapshot(now, windowSeconds),
            new RuntimeMemoryRecord(
                GC.GetTotalAllocatedBytes(precise: false),
                GC.GetTotalMemory(forceFullCollection: false),
                Environment.WorkingSet,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2)),
            metrics);
    }

    private sealed class SampleRing
    {
        private readonly object _gate = new();
        private readonly long[] _timestamps;
        private readonly double[] _deltaMilliseconds;
        private readonly double[] _hostWorkMilliseconds;
        private int _count;
        private int _next;

        public SampleRing(int capacity)
        {
            _timestamps = new long[capacity];
            _deltaMilliseconds = new double[capacity];
            _hostWorkMilliseconds = new double[capacity];
        }

        public void Add(double deltaMilliseconds, double hostWorkMilliseconds)
        {
            if (!double.IsFinite(deltaMilliseconds) || deltaMilliseconds < 0 ||
                !double.IsFinite(hostWorkMilliseconds) || hostWorkMilliseconds < 0)
            {
                return;
            }

            var timestamp = Stopwatch.GetTimestamp();
            lock (_gate)
            {
                _timestamps[_next] = timestamp;
                _deltaMilliseconds[_next] = deltaMilliseconds;
                _hostWorkMilliseconds[_next] = hostWorkMilliseconds;
                _next = (_next + 1) % _timestamps.Length;
                _count = Math.Min(_count + 1, _timestamps.Length);
            }
        }

        public RuntimeFrameStatistics Snapshot(long now, double windowSeconds)
        {
            var minimumTimestamp = now - (long)(windowSeconds * Stopwatch.Frequency);
            double[] deltas;
            double[] hostWork;
            long earliestTimestamp = now;
            lock (_gate)
            {
                var matching = 0;
                for (var offset = 0; offset < _count; offset++)
                {
                    var index = (_next - 1 - offset + _timestamps.Length) % _timestamps.Length;
                    if (_timestamps[index] < minimumTimestamp)
                    {
                        break;
                    }
                    matching++;
                }

                deltas = new double[matching];
                hostWork = new double[matching];
                for (var offset = 0; offset < matching; offset++)
                {
                    var index = (_next - matching + offset + _timestamps.Length) % _timestamps.Length;
                    deltas[offset] = _deltaMilliseconds[index];
                    hostWork[offset] = _hostWorkMilliseconds[index];
                    earliestTimestamp = Math.Min(earliestTimestamp, _timestamps[index]);
                }
            }

            var observedSeconds = deltas.Length == 0
                ? 0
                : Math.Max(0, (now - earliestTimestamp) / (double)Stopwatch.Frequency);
            return new RuntimeFrameStatistics(
                deltas.Length,
                observedSeconds,
                Distribution(deltas),
                Distribution(hostWork),
                deltas.Count(value => value > 16.667),
                deltas.Count(value => value > 33.333),
                deltas.Count(value => value > 50.0));
        }

        private static RuntimeDistribution Distribution(double[] values)
        {
            if (values.Length == 0)
            {
                return new RuntimeDistribution(0, 0, 0, 0, 0, 0);
            }

            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            return new RuntimeDistribution(
                values.Average(),
                Percentile(sorted, 0.50),
                Percentile(sorted, 0.95),
                Percentile(sorted, 0.99),
                sorted[0],
                sorted[^1]);
        }

        private static double Percentile(IReadOnlyList<double> sorted, double percentile)
        {
            var rank = percentile * (sorted.Count - 1);
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);
            if (lower == upper)
            {
                return sorted[lower];
            }

            var fraction = rank - lower;
            return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
        }
    }
}

internal sealed record RuntimePerformanceRecord(
    double WindowSeconds,
    RuntimeFrameStatistics Frames,
    RuntimeFrameStatistics PhysicsFrames,
    RuntimeMemoryRecord Memory,
    object Metrics);

internal sealed record RuntimeFrameStatistics(
    int SampleCount,
    double ObservedSeconds,
    RuntimeDistribution DeltaMilliseconds,
    RuntimeDistribution HostWorkMilliseconds,
    int Over16Milliseconds,
    int Over33Milliseconds,
    int Over50Milliseconds);

internal sealed record RuntimeDistribution(
    double Average,
    double P50,
    double P95,
    double P99,
    double Minimum,
    double Maximum);

internal sealed record RuntimeMemoryRecord(
    long TotalAllocatedBytes,
    long ManagedHeapBytes,
    long WorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);
