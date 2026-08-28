namespace LXFramework.Core.Tests;

public sealed class LubanRuntimeTests
{
    [Fact]
    public void ByteBuf_ReadsLubanBinaryPrimitives()
    {
        var buffer = Luban.ByteBuf.Wrap(
            [3, (byte)'L', (byte)'X', (byte)'F', 0x80, 0x64, 1]);

        Assert.Equal("LXF", buffer.ReadString());
        Assert.Equal(100, buffer.ReadInt());
        Assert.True(buffer.ReadBool());
        Assert.True(buffer.Empty);
    }

    [Fact]
    public void ByteBuf_RejectsTruncatedPayload()
    {
        var buffer = Luban.ByteBuf.Wrap([4, (byte)'L', (byte)'X']);

        Assert.Throws<Luban.SerializationException>(() => buffer.ReadString());
    }

    [Fact]
    public void StringUtil_FormatsCollectionsDeterministically()
    {
        var value = Luban.StringUtil.CollectionToString(new[] { "alpha", "beta" });

        Assert.Equal("[\"alpha\", \"beta\"]", value);
    }
}
