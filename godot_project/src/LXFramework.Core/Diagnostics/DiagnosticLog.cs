namespace LX.Core.Diagnostics;

/// <summary>诊断消息的严重级别；级别越高，越需要人工或自动化立即关注。</summary>
public enum DiagnosticSeverity
{
    /// <summary>仅用于高频执行轨迹，发布构建通常不需要长期保留。</summary>
    Trace,

    /// <summary>用于开发期定位状态变化，不表示运行失败。</summary>
    Debug,

    /// <summary>表示正常但值得记录的框架行为。</summary>
    Information,

    /// <summary>表示框架已降级或发现潜在问题，但当前操作仍可继续。</summary>
    Warning,

    /// <summary>表示当前操作失败，需要检查错误与关联上下文。</summary>
    Error,

    /// <summary>表示框架无法可靠继续运行，应尽快保存快照并停止当前流程。</summary>
    Critical,
}

/// <summary>一条可序列化、带顺序号和结构化字段的诊断消息。</summary>
public sealed record DiagnosticEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    DiagnosticSeverity Severity,
    string Category,
    string Message,
    string? Exception,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// 有固定容量的线程安全诊断日志。旧消息会被自动淘汰，避免调试功能无限占用内存。
/// </summary>
public sealed class DiagnosticLog
{
    private readonly object _gate = new();
    private readonly Queue<DiagnosticEntry> _entries;
    private readonly int _capacity;
    private long _sequence;

    /// <summary>创建诊断日志并指定最多保留的消息数量。</summary>
    public DiagnosticLog(int capacity = 256)
    {
        _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries = new Queue<DiagnosticEntry>(capacity);
    }

    /// <summary>最多保留的消息数量。</summary>
    public int Capacity => _capacity;

    /// <summary>当前保留的消息数量。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>追加一条诊断消息，并返回写入后的不可变消息记录。</summary>
    public DiagnosticEntry Write(
        DiagnosticSeverity severity,
        string category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Diagnostic categories cannot be empty.", nameof(category));
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Diagnostic messages cannot be empty.", nameof(message));
        }

        lock (_gate)
        {
            var entry = new DiagnosticEntry(
                ++_sequence,
                DateTimeOffset.UtcNow,
                severity,
                category.Trim(),
                message.Trim(),
                exception?.ToString(),
                fields is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(fields, StringComparer.Ordinal));
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
            return entry;
        }
    }

    /// <summary>按写入顺序返回当前保留消息的稳定快照。</summary>
    public IReadOnlyList<DiagnosticEntry> Snapshot(DiagnosticSeverity minimum = DiagnosticSeverity.Trace)
    {
        if (!Enum.IsDefined(minimum))
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }

        lock (_gate)
        {
            return _entries.Where(entry => entry.Severity >= minimum).ToArray();
        }
    }

    /// <summary>清除已保留的消息；顺序号不会回退，便于识别清除前后的边界。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
