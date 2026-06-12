using AR.Iec61850.Asn1;

namespace AR.Iec61850.Mms;

public static class MmsPresentation
{
    public static byte[] WrapIsoPresentationPData(byte[] mmsPdu, int presentationContextId = 3)
    {
        var contextId = Integer(presentationContextId);
        var singleAsn1Type = BerWriter.EncodeTlv(0xA0, mmsPdu);
        var pdvList = BerWriter.EncodeTlv(0x30, Concat(contextId, singleAsn1Type));
        var fullyEncodedData = BerWriter.EncodeTlv(0x61, pdvList);

        return Concat([0x01, 0x00, 0x01, 0x00], fullyEncodedData);
    }

    public static byte[] StripPresentationPrefix(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
            return Array.Empty<byte>();

        var span = payload.Span;

        if (payload.Length > 5 &&
            span[0] == 0x01 &&
            span[1] == 0x00 &&
            span[2] == 0x01 &&
            span[3] == 0x00 &&
            span[4] == 0x61 &&
            TryExtractMmsFromFullyEncodedData(payload[4..], out var mms))
        {
            return mms;
        }

        if (payload.Length > 3 &&
            span[0] == 0x01 &&
            span[1] == 0x00 &&
            span[2] == 0x61 &&
            TryExtractMmsFromFullyEncodedData(payload[2..], out mms))
        {
            return mms;
        }

        if (span[0] == 0x61 && TryExtractMmsFromFullyEncodedData(payload, out mms))
            return mms;

        if (payload.Length > 2 && span[0] == 0x01 && span[1] == 0x00 && (span[2] & 0xE0) == 0xA0)
            return payload[2..].ToArray();

        return payload.ToArray();
    }

    private static bool TryExtractMmsFromFullyEncodedData(ReadOnlyMemory<byte> payload, out byte[] mms)
    {
        mms = Array.Empty<byte>();

        try
        {
            var offset = 0;
            if (!BerReader.TryReadTlv(payload, ref offset, out var outer) || outer.EncodedTag != 0x61)
                return false;

            foreach (var pdvList in BerReader.ReadChildren(outer.Value))
            {
                if (pdvList.EncodedTag != 0x30)
                    continue;

                foreach (var item in BerReader.ReadChildren(pdvList.Value))
                {
                    if (item.EncodedTag == 0xA0)
                    {
                        mms = item.Value.ToArray();
                        return mms.Length > 0;
                    }
                }
            }
        }
        catch (BerFormatException)
        {
            return false;
        }

        return false;
    }

    internal static byte[] Integer(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        return BerWriter.EncodeTlv(0x02, EncodeIntegerContent(value));
    }

    internal static byte[] EncodeIntegerContent(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        if (value <= 0x7F)
            return [(byte)value];

        if (value <= 0xFF)
            return [0x00, (byte)value];

        if (value <= 0x7FFF)
            return [(byte)(value >> 8), (byte)value];

        if (value <= 0xFFFF)
            return [0x00, (byte)(value >> 8), (byte)value];

        return [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    internal static byte[] VisibleString(string text)
        => BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii(text));

    internal static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
            length += part.Length;

        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
