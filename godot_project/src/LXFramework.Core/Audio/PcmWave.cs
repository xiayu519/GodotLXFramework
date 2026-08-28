using System.Buffers.Binary;

namespace LX.Core.Audio;

public sealed record PcmWave(int SampleRate, int Channels, int BitsPerSample, byte[] Data)
{
    public int BytesPerFrame => Channels * (BitsPerSample / 8);
    public int SampleFrames => Data.Length / BytesPerFrame;
    public double DurationSeconds => (double)SampleFrames / SampleRate;

    public static PcmWave Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 44 || !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Audio file is not a RIFF/WAVE stream.");
        }

        ushort format = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        ReadOnlySpan<byte> pcm = default;
        var hasFormat = false;
        var offset = 12;
        while (offset <= bytes.Length - 8)
        {
            var chunkId = bytes.Slice(offset, 4);
            var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4)));
            var dataOffset = offset + 8;
            if (chunkLength < 0 || dataOffset > bytes.Length - chunkLength)
            {
                throw new InvalidDataException("WAVE chunk extends beyond the file.");
            }

            var chunk = bytes.Slice(dataOffset, chunkLength);
            if (chunkId.SequenceEqual("fmt "u8))
            {
                if (chunk.Length < 16)
                {
                    throw new InvalidDataException("WAVE format chunk is too short.");
                }

                format = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
                hasFormat = true;
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                pcm = chunk;
            }

            offset = checked(dataOffset + chunkLength + (chunkLength & 1));
        }

        if (!hasFormat || pcm.IsEmpty)
        {
            throw new InvalidDataException("WAVE stream is missing its format or PCM data chunk.");
        }
        if (format != 1 || channels is < 1 or > 2 || bitsPerSample != 16 ||
            sampleRate is < 8_000 or > 192_000)
        {
            throw new InvalidDataException(
                $"Only mono/stereo 16-bit PCM WAVE is supported; got format {format}, {channels} channels, {bitsPerSample} bits, {sampleRate} Hz.");
        }

        var expectedBlockAlign = channels * bitsPerSample / 8;
        if (blockAlign != expectedBlockAlign || pcm.Length % blockAlign != 0)
        {
            throw new InvalidDataException("WAVE PCM data is not aligned to complete sample frames.");
        }

        return new PcmWave((int)sampleRate, channels, bitsPerSample, pcm.ToArray());
    }
}
