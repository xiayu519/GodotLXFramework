using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Luban;

/// <summary>
/// Read-only Luban binary buffer used by generated <c>cs-bin</c> tables.
/// </summary>
public sealed class ByteBuf
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public ByteBuf(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Bytes = bytes;
        WriterIndex = bytes.Length;
    }

    public static ByteBuf Wrap(byte[] bytes) => new(bytes);

    public byte[] Bytes { get; }

    public int ReaderIndex { get; private set; }

    public int WriterIndex { get; }

    public int Remaining => WriterIndex - ReaderIndex;

    public bool Empty => Remaining == 0;

    public bool ReadBool() => ReadByte() != 0;

    public byte ReadByte()
    {
        EnsureRead(1);
        return Bytes[ReaderIndex++];
    }

    public short ReadShort()
    {
        EnsureRead(1);
        var head = Bytes[ReaderIndex];
        if (head < 0x80)
        {
            ReaderIndex++;
            return (short)head;
        }
        if (head < 0xc0)
        {
            EnsureRead(2);
            var value = ((head & 0x3f) << 8) | Bytes[ReaderIndex + 1];
            ReaderIndex += 2;
            return (short)value;
        }
        if (head == 0xff)
        {
            EnsureRead(3);
            var value = (Bytes[ReaderIndex + 1] << 8) | Bytes[ReaderIndex + 2];
            ReaderIndex += 3;
            return (short)value;
        }

        throw InvalidEncoding("Int16");
    }

    public short ReadFshort()
    {
        var span = ReadSpan(sizeof(short));
        return BinaryPrimitives.ReadInt16LittleEndian(span);
    }

    public int ReadInt() => unchecked((int)ReadUint());

    public uint ReadUint()
    {
        EnsureRead(1);
        var head = Bytes[ReaderIndex];
        uint value;
        int size;
        if (head < 0x80)
        {
            value = head;
            size = 1;
        }
        else if (head < 0xc0)
        {
            EnsureRead(2);
            value = ((uint)(head & 0x3f) << 8) | Bytes[ReaderIndex + 1];
            size = 2;
        }
        else if (head < 0xe0)
        {
            EnsureRead(3);
            value = ((uint)(head & 0x1f) << 16) |
                    ((uint)Bytes[ReaderIndex + 1] << 8) |
                    Bytes[ReaderIndex + 2];
            size = 3;
        }
        else if (head < 0xf0)
        {
            EnsureRead(4);
            value = ((uint)(head & 0x0f) << 24) |
                    ((uint)Bytes[ReaderIndex + 1] << 16) |
                    ((uint)Bytes[ReaderIndex + 2] << 8) |
                    Bytes[ReaderIndex + 3];
            size = 4;
        }
        else
        {
            EnsureRead(5);
            value = ((uint)Bytes[ReaderIndex + 1] << 24) |
                    ((uint)Bytes[ReaderIndex + 2] << 16) |
                    ((uint)Bytes[ReaderIndex + 3] << 8) |
                    Bytes[ReaderIndex + 4];
            size = 5;
        }

        ReaderIndex += size;
        return value;
    }

    public int ReadFint()
    {
        var span = ReadSpan(sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(span);
    }

    public long ReadLong() => unchecked((long)ReadUlong());

    public ulong ReadUlong()
    {
        EnsureRead(1);
        var head = Bytes[ReaderIndex];
        ulong value;
        int size;
        if (head < 0x80)
        {
            value = head;
            size = 1;
        }
        else if (head < 0xc0)
        {
            EnsureRead(2);
            value = ((ulong)(head & 0x3f) << 8) | Bytes[ReaderIndex + 1];
            size = 2;
        }
        else if (head < 0xe0)
        {
            EnsureRead(3);
            value = ((ulong)(head & 0x1f) << 16) |
                    ((ulong)Bytes[ReaderIndex + 1] << 8) |
                    Bytes[ReaderIndex + 2];
            size = 3;
        }
        else if (head < 0xf0)
        {
            EnsureRead(4);
            value = ((ulong)(head & 0x0f) << 24) |
                    ((ulong)Bytes[ReaderIndex + 1] << 16) |
                    ((ulong)Bytes[ReaderIndex + 2] << 8) |
                    Bytes[ReaderIndex + 3];
            size = 4;
        }
        else if (head < 0xf8)
        {
            EnsureRead(5);
            value = ((ulong)(head & 0x07) << 32) | ReadBigEndianTail(1, 4);
            size = 5;
        }
        else if (head < 0xfc)
        {
            EnsureRead(6);
            value = ((ulong)(head & 0x03) << 40) | ReadBigEndianTail(1, 5);
            size = 6;
        }
        else if (head < 0xfe)
        {
            EnsureRead(7);
            value = ((ulong)(head & 0x01) << 48) | ReadBigEndianTail(1, 6);
            size = 7;
        }
        else if (head == 0xfe)
        {
            EnsureRead(8);
            value = ReadBigEndianTail(1, 7);
            size = 8;
        }
        else
        {
            EnsureRead(9);
            value = ReadBigEndianTail(1, 8);
            size = 9;
        }

        ReaderIndex += size;
        return value;
    }

    public long ReadFlong()
    {
        var span = ReadSpan(sizeof(long));
        return BinaryPrimitives.ReadInt64LittleEndian(span);
    }

    public double ReadLongAsNumber() => ReadLong();

    public float ReadFloat()
    {
        var bits = BinaryPrimitives.ReadInt32LittleEndian(ReadSpan(sizeof(float)));
        return BitConverter.Int32BitsToSingle(bits);
    }

    public double ReadDouble()
    {
        var bits = BinaryPrimitives.ReadInt64LittleEndian(ReadSpan(sizeof(double)));
        return BitConverter.Int64BitsToDouble(bits);
    }

    public int ReadSize()
    {
        var value = ReadUint();
        if (value > int.MaxValue)
        {
            throw new SerializationException($"Luban collection size '{value}' exceeds Int32.MaxValue.");
        }
        return (int)value;
    }

    public int ReadSint()
    {
        var value = ReadUint();
        return unchecked((int)((value >> 1) ^ (uint)-(int)(value & 1)));
    }

    public long ReadSlong()
    {
        var value = ReadUlong();
        return unchecked((long)((value >> 1) ^ (ulong)-(long)(value & 1)));
    }

    public string ReadString()
    {
        var length = ReadSize();
        if (length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Utf8.GetString(ReadSpan(length));
        }
        catch (DecoderFallbackException exception)
        {
            throw new SerializationException("Luban string contains invalid UTF-8.", exception);
        }
    }

    public byte[] ReadBytes() => ReadSpan(ReadSize()).ToArray();

    public Complex ReadComplex() => new(ReadDouble(), ReadDouble());

    public Vector2 ReadVector2() => new(ReadFloat(), ReadFloat());

    public Vector3 ReadVector3() => new(ReadFloat(), ReadFloat(), ReadFloat());

    public Vector4 ReadVector4() => new(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());

    public Quaternion ReadQuaternion() => new(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());

    public Matrix4x4 ReadMatrix4x4() => new(
        ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat(),
        ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat(),
        ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat(),
        ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());

    private ReadOnlySpan<byte> ReadSpan(int length)
    {
        EnsureRead(length);
        var span = Bytes.AsSpan(ReaderIndex, length);
        ReaderIndex += length;
        return span;
    }

    private ulong ReadBigEndianTail(int offset, int count)
    {
        ulong value = 0;
        for (var index = 0; index < count; index++)
        {
            value = (value << 8) | Bytes[ReaderIndex + offset + index];
        }
        return value;
    }

    private void EnsureRead(int length)
    {
        if (length < 0 || ReaderIndex > WriterIndex - length)
        {
            throw new SerializationException(
                $"Luban binary payload ended at byte {ReaderIndex}; requested {length} more byte(s)." );
        }
    }

    private SerializationException InvalidEncoding(string type) =>
        new($"Luban binary payload contains an invalid {type} prefix at byte {ReaderIndex}.");
}
