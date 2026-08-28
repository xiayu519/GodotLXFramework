namespace LX.Core.Data;

public interface IDataRecord<out TId>
{
    TId Id { get; }
}

public sealed class DataCatalog<TId, TValue>
    where TId : notnull
    where TValue : IDataRecord<TId>
{
    private readonly Dictionary<TId, TValue> _records;

    public DataCatalog(IEnumerable<TValue> records, IEqualityComparer<TId>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        _records = new Dictionary<TId, TValue>(comparer);
        foreach (var record in records)
        {
            if (!_records.TryAdd(record.Id, record))
            {
                throw new InvalidDataException($"Duplicate data ID '{record.Id}'.");
            }
        }
    }

    public int Count => _records.Count;

    public IReadOnlyCollection<TValue> Records => _records.Values;

    public TValue this[TId id] => _records.TryGetValue(id, out var value)
        ? value
        : throw new KeyNotFoundException($"Data ID '{id}' was not found.");

    public bool TryGet(TId id, out TValue value) => _records.TryGetValue(id, out value!);
}
