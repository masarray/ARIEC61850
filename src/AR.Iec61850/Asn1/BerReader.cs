using System.Buffers.Binary;
using System.Text;

namespace AR.Iec61850.Asn1;

public static class BerReader
{
    public static bool TryReadTlv(ReadOnlyMemory<byte> source, ref int offset, out BerTlv tlv)
    {
        tlv = default;

        if (offset < 0 || offset >= source.Length)
            return false;

        var span = source.Span;
        var encodedTag = span[offset++];
        var tagNumber = encodedTag & 0x1F;

        if (tagNumber == 0x1F)
            return false;

        if (offset >= source.Length)
            return false;

        var lengthByte = span[offset++];
        int length;

        if ((lengthByte & 0x80) == 0)
        {
            length = lengthByte;
        }
        else
        {
            var lengthBytes = lengthByte & 0x7F;
            if (lengthBytes is 0 or > 4 || offset + lengthBytes > source.Length)
                return false;

            length = 0;
            for (var i = 0; i < lengthBytes; i++)
                length = (length << 8) | span[offset++];
        }

        if (length < 0 || offset + length > source.Length)
            return false;

        tlv = new BerTlv(
            encodedTag,
            (BerClass)((encodedTag >> 6) & 0x03),
            (encodedTag & 0x20) != 0,
            tagNumber,
            source.Slice(offset, length));

        offset += length;
        return true;
    }

    public static IReadOnlyList<BerTlv> ReadChildren(ReadOnlyMemory<byte> source)
    {
        var result = new List<BerTlv>();
        var offset = 0;

        while (offset < source.Length)
        {
            if (!TryReadTlv(source, ref offset, out var tlv))
                throw new BerFormatException($"Invalid BER TLV at offset {offset}.");

            result.Add(tlv);
        }

        return result;
    }

    public static string ReadAsciiString(BerTlv tlv)
        => tlv.Value.IsEmpty ? string.Empty : Encoding.ASCII.GetString(tlv.Value.Span);

    public static bool? ReadBoolean(BerTlv tlv)
        => tlv.Value.Length == 1 ? tlv.Value.Span[0] != 0 : null;

    public static ulong? ReadUnsignedInteger(BerTlv tlv)
    {
        var span = tlv.Value.Span;

        if (span.Length > 8)
            return null;

        ulong value = 0;
        for (var i = 0; i < span.Length; i++)
            value = (value << 8) | span[i];

        return value;
    }

    public static long? ReadSignedInteger(BerTlv tlv)
    {
        var span = tlv.Value.Span;

        if (span.Length == 0)
            return 0;

        if (span.Length > 8)
            return null;

        if (span.Length == 8)
            return BinaryPrimitives.ReadInt64BigEndian(span);

        long value = 0;
        for (var i = 0; i < span.Length; i++)
            value = (value << 8) | span[i];

        if ((span[0] & 0x80) != 0)
            value -= 1L << (span.Length * 8);

        return value;
    }

    public static ushort? ReadUInt16(BerTlv tlv)
    {
        var value = ReadUnsignedInteger(tlv);
        return value <= ushort.MaxValue ? (ushort)value.Value : null;
    }

    public static uint? ReadUInt32(BerTlv tlv)
    {
        var value = ReadUnsignedInteger(tlv);
        return value <= uint.MaxValue ? (uint)value.Value : null;
    }

    public static byte[] ReadBytes(BerTlv tlv)
        => tlv.Value.ToArray();

    public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> source)
    {
        if (source.Length != 4)
            throw new ArgumentException("A 32-bit integer requires exactly four bytes.", nameof(source));

        return BinaryPrimitives.ReadUInt32BigEndian(source);
    }
}
