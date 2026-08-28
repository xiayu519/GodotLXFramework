using LX.Core.World;

namespace LXFramework.Core.Tests;

public sealed class WorldEventJournalTests
{
    [Fact]
    public void CaptureAndRestore_PreservesCompletedEventsDeterministically()
    {
        var source = new WorldEventJournal();
        source.TryComplete(new WorldEventId("map.town:gate_opened"));
        source.TryComplete(new WorldEventId("map.town:chest_01"));

        var snapshot = source.Capture();
        var restored = new WorldEventJournal();
        restored.Restore(snapshot);

        Assert.Equal(2, restored.Count);
        Assert.True(restored.IsCompleted(new WorldEventId("map.town:gate_opened")));
        Assert.Equal(snapshot.CompletedEventIds.Order(), snapshot.CompletedEventIds);
    }

    [Fact]
    public void Restore_IsAtomicWhenSnapshotContainsInvalidId()
    {
        var journal = new WorldEventJournal();
        var retained = new WorldEventId("map.retained");
        journal.TryComplete(retained);

        Assert.Throws<ArgumentException>(() => journal.Restore(
            new WorldEventJournalSnapshot(["map.valid", "Invalid Event"])));

        Assert.True(journal.IsCompleted(retained));
        Assert.Equal(1, journal.Count);
    }

    [Fact]
    public void TryComplete_PreventsDuplicateOneShotActivation()
    {
        var journal = new WorldEventJournal();
        var eventId = new WorldEventId("map.trigger_01");

        Assert.True(journal.TryComplete(eventId));
        Assert.False(journal.TryComplete(eventId));
        Assert.True(journal.Reset(eventId));
        Assert.True(journal.TryComplete(eventId));
    }
}
