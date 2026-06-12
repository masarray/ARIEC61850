using System.Buffers.Binary;

namespace AR.Iec61850.Ethernet;

public static class EthernetFrameCodec
{
    public static byte[] Encode(EthernetFrame frame)
    {
        var headerLength = frame.Vlan.HasValue ? 18 : 14;
        var bytes = new byte[headerLength + frame.Payload.Length];
        var span = bytes.AsSpan();

        frame.Destination.CopyTo(span[..6]);
        frame.Source.CopyTo(span.Slice(6, 6));

        var offset = 12;
        if (frame.Vlan is { } vlan)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset, 2), EthernetConstants.VlanTagEtherType);
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset + 2, 2), vlan.ToTagControlInformation());
            offset += 4;
        }

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset, 2), frame.EtherType);
        offset += 2;

        frame.Payload.Span.CopyTo(span[offset..]);
        return bytes;
    }

    public static bool TryDecode(ReadOnlyMemory<byte> bytes, out EthernetFrame frame)
    {
        frame = null!;

        if (bytes.Length < 14)
            return false;

        var span = bytes.Span;
        var destination = new MacAddress(span[..6]);
        var source = new MacAddress(span.Slice(6, 6));
        var etherType = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(12, 2));
        VlanTag? vlan = null;
        var payloadOffset = 14;

        if (etherType == EthernetConstants.VlanTagEtherType)
        {
            if (bytes.Length < 18)
                return false;

            vlan = VlanTag.FromTagControlInformation(BinaryPrimitives.ReadUInt16BigEndian(span.Slice(14, 2)));
            etherType = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(16, 2));
            payloadOffset = 18;
        }

        frame = new EthernetFrame(destination, source, etherType, vlan, bytes[payloadOffset..]);
        return true;
    }
}
