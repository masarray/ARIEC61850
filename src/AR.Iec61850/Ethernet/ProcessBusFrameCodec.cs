using System.Buffers.Binary;

namespace AR.Iec61850.Ethernet;

public static class ProcessBusFrameCodec
{
    public const int HeaderLength = 8;

    public static byte[] EncodePayload(
        ushort appId,
        ReadOnlySpan<byte> apdu,
        ushort reserved1 = 0,
        ushort reserved2 = 0)
    {
        var declaredLength = checked((ushort)(HeaderLength + apdu.Length));
        var bytes = new byte[declaredLength];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span[..2], appId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), declaredLength);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), reserved1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), reserved2);
        apdu.CopyTo(span[HeaderLength..]);

        return bytes;
    }

    public static EthernetFrame EncodeEthernetFrame(
        MacAddress destination,
        MacAddress source,
        ushort etherType,
        ushort appId,
        ReadOnlySpan<byte> apdu,
        VlanTag? vlan = null,
        ushort reserved1 = 0,
        ushort reserved2 = 0)
    {
        return new EthernetFrame(
            destination,
            source,
            etherType,
            vlan,
            EncodePayload(appId, apdu, reserved1, reserved2));
    }

    public static bool TryDecode(EthernetFrame ethernet, out ProcessBusFrame frame)
    {
        frame = null!;

        if (ethernet.Payload.Length < HeaderLength)
            return false;

        var span = ethernet.Payload.Span;
        var appId = BinaryPrimitives.ReadUInt16BigEndian(span[..2]);
        var declaredLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2));
        var reserved1 = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4, 2));
        var reserved2 = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6, 2));

        var availableApduLength = ethernet.Payload.Length - HeaderLength;
        var declaredApduLength = declaredLength >= HeaderLength
            ? declaredLength - HeaderLength
            : availableApduLength;

        if (declaredApduLength > availableApduLength)
            return false;

        frame = new ProcessBusFrame(
            ethernet,
            appId,
            declaredLength,
            reserved1,
            reserved2,
            ethernet.Payload.Slice(HeaderLength, declaredApduLength));

        return true;
    }
}
