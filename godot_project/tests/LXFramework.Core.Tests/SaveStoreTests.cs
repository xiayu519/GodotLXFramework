using System.Text.Json.Nodes;
using LX.Core.Persistence;

namespace LXFramework.Core.Tests;

public sealed class SaveStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "lx-save-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Slots_CanBeListedAndDeletedWithoutLoadingPayload()
    {
        var store = new SaveStore<TestSave>(_directory, 1);
        await store.SaveAsync("slot_a", new TestSave("hero", 7));

        var metadata = Assert.Single(store.ListSlots());
        Assert.Equal("slot_a", metadata.Slot);
        Assert.Equal(1, metadata.SchemaVersion);
        Assert.True(metadata.FileSizeBytes > 0);
        Assert.True(await store.DeleteAsync("slot_a"));
        Assert.Empty(store.ListSlots());
        Assert.False(await store.DeleteAsync("slot_a"));
    }

    [Fact]
    public async Task Load_UsesVerifiedBackupWhenPrimaryIsCorrupt()
    {
        var store = new SaveStore<TestSave>(_directory, 1);
        await store.SaveAsync("slot_1", new TestSave("first", 10));
        await store.SaveAsync("slot_1", new TestSave("second", 20));
        await File.WriteAllTextAsync(Path.Combine(_directory, "slot_1.json"), "corrupt");

        var result = await store.LoadAsync("slot_1");

        Assert.True(result.IsSuccess);
        Assert.Equal(SaveSource.Backup, result.Value.Source);
        Assert.Equal(new TestSave("first", 10), result.Value.State);
    }

    [Fact]
    public async Task Load_AppliesEveryRequiredMigration()
    {
        var oldStore = new SaveStore<TestSaveV1>(_directory, 1);
        await oldStore.SaveAsync("slot_2", new TestSaveV1("hunter"));
        var currentStore = new SaveStore<TestSave>(
            _directory,
            2,
            [new AddMoneyMigration()]);

        var result = await currentStore.LoadAsync("slot_2");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.StoredVersion);
        Assert.Equal(2, result.Value.LoadedVersion);
        Assert.Equal(new TestSave("hunter", 100), result.Value.State);
    }

    [Fact]
    public async Task ConcurrentWrites_AreSerializedAndLeaveAReadableSlot()
    {
        var store = new SaveStore<TestSave>(_directory, 1);

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => store.SaveAsync("quick", new TestSave($"save-{index}", index)).AsTask()));

        var result = await store.LoadAsync("quick");
        Assert.True(result.IsSuccess);
        Assert.InRange(result.Value.State.Money, 0, 7);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp.*"));
    }

    public void Dispose()
    {
        var fullPath = Path.GetFullPath(_directory);
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lx-save-tests"));
        if (fullPath.StartsWith(expectedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private sealed record TestSave(string Name, int Money);

    private sealed record TestSaveV1(string Name);

    private sealed class AddMoneyMigration : ISaveMigration
    {
        public int FromVersion => 1;

        public int ToVersion => 2;

        public JsonNode Migrate(JsonNode payload)
        {
            payload["money"] = 100;
            return payload;
        }
    }
}
