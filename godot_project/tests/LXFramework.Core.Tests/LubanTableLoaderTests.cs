using System.Text;
using LX.Core.Data;
using Luban;

namespace LXFramework.Core.Tests;

public sealed class LubanTableLoaderTests
{
    [Fact]
    public void Load_AdaptsGeneratedTableFactory()
    {
        var tables = LubanTableLoader.Load(
            name => name == "design_probe"
                ? [1, 12, .. Encoding.UTF8.GetBytes("lx_framework"), 100, 1]
                : throw new InvalidOperationException(name),
            loader => new ProbeTables(loader));

        Assert.Equal("lx_framework", tables.Id);
        Assert.Equal(100, tables.Priority);
        Assert.True(tables.Enabled);
    }

    [Fact]
    public void Load_RejectsUnsafeGeneratedTableName()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            LubanTableLoader.Load(
                _ => [1],
                loader => loader("../outside")));

        Assert.Contains("unsafe table name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsEmptyBinaryPayload()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            LubanTableLoader.Load(
                _ => [],
                loader => loader("design_probe")));

        Assert.Contains("empty binary payload", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ProbeTables
    {
        public ProbeTables(Func<string, ByteBuf> loader)
        {
            var buffer = loader("design_probe");
            Assert.Equal(1, buffer.ReadSize());
            Id = buffer.ReadString();
            Priority = buffer.ReadInt();
            Enabled = buffer.ReadBool();
        }

        public string Id { get; }

        public int Priority { get; }

        public bool Enabled { get; }
    }
}
