namespace LX.Core.Diagnostics;

public sealed class MetricRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, double> _gauges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

    public void SetGauge(string name, double value)
    {
        ValidateName(name);
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        lock (_gate)
        {
            _gauges[name] = value;
        }
    }

    public long Increment(string name, long amount = 1)
    {
        ValidateName(name);
        lock (_gate)
        {
            _counters.TryGetValue(name, out var current);
            var next = checked(current + amount);
            _counters[name] = next;
            return next;
        }
    }

    public MetricSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new MetricSnapshot(
                new Dictionary<string, double>(_gauges, StringComparer.Ordinal),
                new Dictionary<string, long>(_counters, StringComparer.Ordinal));
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Metric names cannot be empty.", nameof(name));
        }
    }
}

public sealed record MetricSnapshot(
    IReadOnlyDictionary<string, double> Gauges,
    IReadOnlyDictionary<string, long> Counters);
