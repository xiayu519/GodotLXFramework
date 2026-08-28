using LX.Core.Data;

namespace LXFramework.Core.Tests;

public sealed class DataCatalogTests
{
    [Fact]
    public void Catalog_RejectsDuplicateIds()
    {
        var records = new[]
        {
            new Item("engine", 100),
            new Item("engine", 200),
        };

        Assert.Throws<InvalidDataException>(() => new DataCatalog<string, Item>(records));
    }

    private sealed record Item(string Id, int Price) : IDataRecord<string>;
}
