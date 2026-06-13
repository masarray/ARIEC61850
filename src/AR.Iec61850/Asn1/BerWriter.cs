using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace AR.Iec61850.Asn1;

public sealed class BerWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public int Length => _buffer.WrittenCount;

    public void WriteTlv(BerClass berClass, bool constructed, int tagNumber, ReadOnlySpan<byte> value)
    {
        WriteIdentifier(berClass, constructed, tagNumber);
        WriteLength(value.Length);
        WriteBytes(value);
    }

    public void WriteTlv(byte encodedTag, ReadOnlySpan<byte> value)
    {
        WriteByte(encodedTag);
        WriteLength(value.Length);
        WriteBytes(value);
    }

    public void WriteRaw(ReadOnlySpan<byte> bytes)
        => WriteBytes(bytes);

    public byte[] ToArray()
        => _buffer.WrittenSpan.ToArray();

    public static byte[] EncodeTlv(BerClass berClass, bool constructed, int tagNumber, ReadOnlySpan<byte> value)
    {
        var writer = new BerWriter();
        writer.WriteTlv(berClass, constructed, tagNumber, value);
        return writer.ToArray();
    }

    public static byte[] EncodeTlv(byte encodedTag, ReadOnlySpan<byte> value)
    {
        var writer = new BerWriter();
        writer.WriteTlv(encodedTag, value);
        return writer.ToArray();
    }

    public static byte EncodeIdentifier(BerClass berClass, bool constructed, int tagNumber)
    {
        if (tagNumber is < 0 or > 30)
            throw new ArgumentOutOfRangeException(nameof(tagNumber), "Use WriteTlv/EncodeTlv for high-tag-number BER identifiers.");

        return (byte)(((byte)berClass << 6) | (constructed ? 0x20 : 0x00) | tagNumber);
    }

    public static byte[] EncodeAscii(string value)
        => Encoding.ASCII.GetBytes(value ?? string.Empty);

    public static byte[] EncodeBoolean(bool value)
        => new[] { value ? (byte)0x01 : (byte)0x00 };

    public static byte[] EncodeUnsignedInteger(ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);

        var first = 0;
        while (first < buffer.Length - 1 && buffer[first] == 0)
            first++;

        return buffer[first..].ToArray();
    }

    public static byte[] EncodeSignedInteger(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);

        var first = 0;
        while (first < buffer.Length - 1)
        {
            var current = buffer[first];
            var next = buffer[first + 1];
            var redundantPositive = current == 0x00 && (next & 0x80) == 0;
            var redundantNegative = current == 0xFF && (next & 0x80) != 0;

            if (!redundantPositive && !redundantNegative)
                break;

            first++;
        }

        return buffer[first..].ToArray();
    }

    public static byte[] EncodeSinglePrecisionFloat(float value)
    {
        Span<byte> buffer = stackalloc byte[5];
        buffer[0] = 0x08;
        BinaryPrimitives.WriteUInt32BigEndian(buffer[1..], unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        return buffer.ToArray();
    }

    public static byte[] EncodeUtcTime(DateTimeOffset value, byte quality = 0)
    {
        var utc = value.ToUniversalTime();
        var seconds = utc.ToUnixTimeSeconds();
        var fractionalSeconds = utc - DateTimeOffset.FromUnixTimeSeconds(seconds);
        var fraction = (uint)Math.Round(fractionalSeconds.TotalSeconds * 16_777_216.0);

        if (fraction >= 16_777_216)
        {
            seconds++;
            fraction = 0;
        }

        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(buffer[..4], checked((uint)seconds));
        buffer[4] = (byte)((fraction >> 16) & 0xFF);
        buffer[5] = (byte)((fraction >> 8) & 0xFF);
        buffer[6] = (byte)(fraction & 0xFF);
        buffer[7] = quality;
        return buffer.ToArray();
    }

    private void WriteIdentifier(BerClass berClass, bool constructed, int tagNumber)
    {
        if (tagNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(tagNumber));

        if (tagNumber <= 30)
        {
            WriteByte(EncodeIdentifier(berClass, constructed, tagNumber));
            return;
        }

        WriteByte((byte)(((byte)berClass << 6) | (constructed ? 0x20 : 0x00) | 0x1F));

        Span<byte> buffer = stackalloc byte[5];
        var count = 0;
        var value = tagNumber;
        do
        {
            buffer[count++] = (byte)(value & 0x7F);
            value >>= 7;
        }
        while (value > 0);

        for (var i = count - 1; i >= 0; i--)
        {
            var b = buffer[i];
            if (i != 0)
                b |= 0x80;
            WriteByte(b);
        }
    }

    private void WriteLength(int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (length < 0x80)
        {
            WriteByte((byte)length);
            return;
        }

        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, length);

        var first = 0;
        while (first < buffer.Length - 1 && buffer[first] == 0)
            first++;

        var count = buffer.Length - first;
        WriteByte((byte)(0x80 | count));
        WriteBytes(buffer[first..]);
    }

    private void WriteByte(byte value)
    {
        var span = _buffer.GetSpan(1);
        span[0] = value;
        _buffer.Advance(1);
    }

    private void WriteBytes(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            return;

        var span = _buffer.GetSpan(value.Length);
        value.CopyTo(span);
        _buffer.Advance(value.Length);
    }
}
