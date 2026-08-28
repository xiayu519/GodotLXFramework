using System.Buffers.Binary;
using LX.Core.Audio;

namespace LXFramework.Core.Tests;

public sealed class PcmWaveTests
{
    [Fact]
    public void Parse_ReadsAlignedStereoPcmAndSkipsUnknownChunks()
    {
        byte[] pcm = [1, 0, 2, 0, 3, 0, 4, 0];
        var wave = BuildWave(pcm, includeJunk: true);

        var parsed = PcmWave.Parse(wave);

        Assert.Equal(48_000, parsed.SampleRate);
        Assert.Equal(2, parsed.Channels);
        Assert.Equal(16, parsed.BitsPerSample);
        Assert.Equal(2, parsed.SampleFrames);
        Assert.Equal(pcm, parsed.Data);
    }

    [Fact]
    public void Parse_RejectsCompressedOrTruncatedStreams()
    {
        var compressed = BuildWave([1, 0, 2, 0], format: 3, channels: 1);
        Assert.Throws<InvalidDataException>(() => PcmWave.Parse(compressed));
        Assert.Throws<InvalidDataException>(() => PcmWave.Parse("not wave"u8));
        Assert.Throws<InvalidDataException>(() => PcmWave.Parse(BuildWave([1, 0, 2], channels: 1)));
    }

    private static byte[] BuildWave(
        byte[] pcm,
        ushort format = 1,
        ushort channels = 2,
        bool includeJunk = false)
    {
        var junkBytes = includeJunk ? 10 : 0;
        var bytes = new byte[44 + junkBytes + pcm.Length];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), format);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 48_000);
        var blockAlign = checked((ushort)(channels * 2));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)(48_000 * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16);
        var dataOffset = 36;
        if (includeJunk)
        {
            "JUNK"u8.CopyTo(bytes.AsSpan(dataOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(dataOffset + 4), 1);
            bytes[dataOffset + 8] = 0x55;
            dataOffset += 10;
        }
        "data"u8.CopyTo(bytes.AsSpan(dataOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(dataOffset + 4), (uint)pcm.Length);
        pcm.CopyTo(bytes.AsSpan(dataOffset + 8));
        return bytes;
    }
}
