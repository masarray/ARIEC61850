using System.Buffers.Binary;

namespace AR.Iec61850.TimeSync.Ptp;

public static class PtpPacketParser
{
    public static bool TryParseEthernetFrame(ReadOnlySpan<byte> ethernetFrame, out PtpFrame frame)
    {
        frame = null!;
        if (ethernetFrame.Length < 14)
            return false;

        var destination = ethernetFrame[..6];
        var offset = 12;
        ushort? outerVlan = null;
        ushort? vlan = null;
        var etherType = BinaryPrimitives.ReadUInt16BigEndian(ethernetFrame.Slice(offset, 2));
        offset += 2;

        if (etherType == PtpConstants.QinQEtherType)
        {
            if (ethernetFrame.Length < offset + 4)
                return false;

            outerVlan = ExtractVlanId(BinaryPrimitives.ReadUInt16BigEndian(ethernetFrame.Slice(offset, 2)));
            etherType = BinaryPrimitives.ReadUInt16BigEndian(ethernetFrame.Slice(offset + 2, 2));
            offset += 4;
        }

        if (etherType == PtpConstants.VlanEtherType)
        {
            if (ethernetFrame.Length < offset + 4)
                return false;

            vlan = ExtractVlanId(BinaryPrimitives.ReadUInt16BigEndian(ethernetFrame.Slice(offset, 2)));
            etherType = BinaryPrimitives.ReadUInt16BigEndian(ethernetFrame.Slice(offset + 2, 2));
            offset += 4;
        }

        if (etherType != PtpConstants.EtherType)
            return false;

        if (!TryParseMessage(ethernetFrame[offset..], out frame))
            return false;

        frame = frame with
        {
            VlanId = vlan,
            OuterVlanId = outerVlan,
            IsPeerDelayMulticast = destination.SequenceEqual(PtpConstants.PeerDelayMulticastMac)
        };
        return true;
    }

    public static bool TryParseMessage(ReadOnlySpan<byte> message, out PtpFrame frame)
    {
        frame = null!;
        if (message.Length < PtpConstants.HeaderLength)
            return false;

        var messageType = (PtpMessageType)(message[0] & 0x0F);
        var transportSpecific = (byte)(message[0] >> 4);
        var version = (byte)(message[1] & 0x0F);
        if (version != PtpConstants.Version2)
            return false;

        var messageLength = BinaryPrimitives.ReadUInt16BigEndian(message.Slice(2, 2));
        if (messageLength < PtpConstants.HeaderLength || message.Length < messageLength)
            return false;

        var domain = message[4];
        var flags = BinaryPrimitives.ReadUInt16BigEndian(message.Slice(6, 2));
        var correction = BinaryPrimitives.ReadInt64BigEndian(message.Slice(8, 8));
        var sourceClock = new ClockIdentity(message.Slice(20, 8));
        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(message.Slice(28, 2));
        var sequenceId = BinaryPrimitives.ReadUInt16BigEndian(message.Slice(30, 2));
        var controlField = message[32];
        var logMessageInterval = unchecked((sbyte)message[33]);
        var body = message.Slice(PtpConstants.HeaderLength, messageLength - PtpConstants.HeaderLength);

        var header = new PtpHeader(
            transportSpecific,
            messageType,
            version,
            messageLength,
            domain,
            flags,
            correction,
            new PtpPortIdentity(sourceClock, sourcePort),
            sequenceId,
            controlField,
            logMessageInterval);

        PtpTimestamp? timestamp = null;
        PtpAnnounceMessage? announce = null;

        if (messageType is PtpMessageType.Sync or PtpMessageType.FollowUp && body.Length >= 10)
            timestamp = PtpTimestamp.Read(body[..10]);

        if (messageType == PtpMessageType.Announce && body.Length >= 30)
        {
            var originTimestamp = PtpTimestamp.Read(body[..10]);
            var currentUtcOffset = BinaryPrimitives.ReadInt16BigEndian(body.Slice(10, 2));
            var priority1 = body[13];
            var clockClass = body[14];
            var clockAccuracy = (PtpClockAccuracy)body[15];
            var variance = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(16, 2));
            var priority2 = body[18];
            var grandmasterIdentity = new ClockIdentity(body.Slice(19, 8));
            var stepsRemoved = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(27, 2));
            var timeSource = (PtpTimeSource)body[29];
            announce = new PtpAnnounceMessage(originTimestamp, currentUtcOffset, priority1, clockClass, clockAccuracy, variance, priority2, grandmasterIdentity, stepsRemoved, timeSource);
            timestamp = originTimestamp;
        }

        frame = new PtpFrame(
            header,
            timestamp,
            announce,
            message[..messageLength].ToArray(),
            body.ToArray());
        return true;
    }

    private static ushort ExtractVlanId(ushort tagControlInformation)
        => (ushort)(tagControlInformation & 0x0FFF);
}
