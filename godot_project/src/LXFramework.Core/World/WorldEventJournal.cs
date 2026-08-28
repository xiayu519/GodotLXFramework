namespace LX.Core.World;

/// <summary>
/// Stable identifier for a persistent world event. IDs are intentionally
/// data-oriented so chunk scenes can be unloaded without losing progress.
/// </summary>
public readonly record struct WorldEventId
{
    public WorldEventId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("World event IDs cannot be empty.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > 128 || !IsValid(normalized))
        {
            throw new ArgumentException(
                "World event IDs must start with a lowercase letter or digit and contain only lowercase letters, digits, '_', '-', '.', or ':'.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;

    private static bool IsValid(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isLowercaseLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            if (index == 0 && !isLowercaseLetter && !isDigit)
            {
                return false;
            }
            if (!isLowercaseLetter && !isDigit && character is not ('_' or '-' or '.' or ':'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Records completed one-shot world events independently of loaded scenes.
/// Product save data owns the captured snapshot and restores it before loading
/// the destination world.
/// </summary>
public sealed class WorldEventJournal
{
    private readonly HashSet<WorldEventId> _completed = [];

    public int Count => _completed.Count;

    public bool IsCompleted(WorldEventId eventId) => _completed.Contains(eventId);

    public bool TryComplete(WorldEventId eventId) => _completed.Add(eventId);

    public bool Reset(WorldEventId eventId) => _completed.Remove(eventId);

    public void Clear() => _completed.Clear();

    public WorldEventJournalSnapshot Capture() => new(
        _completed
            .Select(eventId => eventId.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray());

    public void Restore(WorldEventJournalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.CompletedEventIds);

        var restored = new HashSet<WorldEventId>();
        foreach (var value in snapshot.CompletedEventIds)
        {
            var eventId = new WorldEventId(value);
            if (!restored.Add(eventId))
            {
                throw new InvalidDataException($"World event '{eventId}' appears more than once in the snapshot.");
            }
        }

        _completed.Clear();
        _completed.UnionWith(restored);
    }
}

public sealed record WorldEventJournalSnapshot(IReadOnlyList<string> CompletedEventIds);
