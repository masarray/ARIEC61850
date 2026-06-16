using System.Buffers.Binary;

namespace AR.Iec61850.TimeSync.Ptp;

public static class PtpMessageSerializer
{
    public static byte[] BuildSync(PtpBuildOptions options)
        => BuildTimestampMessage(PtpMessageType.Sync, options, 0x00, options.Timestamp);

    public static byte[] BuildFollowUp(PtpBuildOptions options)
        => BuildTimestampMessage(PtpMessageType.FollowUp, options, 0x02, options.Timestamp);

    public static byte[] BuildPdelayReq(PtpBuildOptions options)
        => BuildTimestampMessage(PtpMessageType.PdelayReq, options, 0x05, options.Timestamp);

    public static byte[] BuildPdelayResp(PtpBuildOptions options, PtpPortIdentity requestSourcePortIdentity)
    {
        var body = new byte[20];
        options.Timestamp.Write(body.AsSpan(0, 10));
        requestSourcePortIdentity.ClockIdentity.CopyTo(body.AsSpan(10, 8));
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(18, 2), requestSourcePortIdentity.PortNumber);
        return BuildMessage(PtpMessageType.PdelayResp, options, 0x05, body);
    }

    public static byte[] BuildPdelayRespFollowUp(PtpBuildOptions options, PtpPortIdentity requestSourcePortIdentity)
    {
        var body = new byte[20];
        options.Timestamp.Write(body.AsSpan(0, 10));
        requestSourcePortIdentity.ClockIdentity.CopyTo(body.AsSpan(10, 8));
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(18, 2), requestSourcePortIdentity.PortNumber);
        return BuildMessage(PtpMessageType.PdelayRespFollowUp, options, 0x05, body);
    }

    public static byte[] BuildAnnounce(PtpBuildOptions options, short currentUtcOffset = 37)
    {
        var body = new byte[30];
        options.Timestamp.Write(body.AsSpan(0, 10));
        BinaryPrimitives.WriteInt16BigEndian(body.AsSpan(10, 2), currentUtcOffset);
        body[13] = options.Priority1;
        body[14] = options.ClockClass;
        body[15] = (byte)options.ClockAccuracy;
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(16, 2), options.OffsetScaledLogVariance);
        body[18] = options.Priority2;
        var grandmaster = options.GrandmasterIdentity == default ? options.SourcePortIdentity.ClockIdentity : options.GrandmasterIdentity;
        grandmaster.CopyTo(body.AsSpan(19, 8));
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(27, 2), options.StepsRemoved);
        body[29] = (byte)options.TimeSource;
        return BuildMessage(PtpMessageType.Announce, options, 0x05, body);
    }

    public static byte[] BuildEthernetFrame(ReadOnlySpan<byte> destinationMac, ReadOnlySpan<byte> sourceMac, ReadOnlySpan<byte> ptpMessage, ushort? vlanId = null)
    {
        if (destinationMac.Length != 6)
            throw new ArgumentException("Destination MAC must be 6 bytes.", nameof(destinationMac));
        if (sourceMac.Length != 6)
            throw new ArgumentException("Source MAC must be 6 bytes.", nameof(sourceMac));

        var headerLength = vlanId.HasValue ? 18 : 14;
        var frame = new byte[headerLength + ptpMessage.Length];
        destinationMac.CopyTo(frame.AsSpan(0, 6));
        sourceMac.CopyTo(frame.AsSpan(6, 6));
        var offset = 12;
        if (vlanId.HasValue)
        {
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), PtpConstants.VlanEtherType);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset + 2, 2), (ushort)(vlanId.Value & 0x0FFF));
            offset += 4;
        }

        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset, 2), PtpConstants.EtherType);
        ptpMessage.CopyTo(frame.AsSpan(offset + 2));
        return frame;
    }

    private static byte[] BuildTimestampMessage(PtpMessageType messageType, PtpBuildOptions options, byte controlField, PtpTimestamp timestamp)
    {
        var body = new byte[10];
        timestamp.Write(body);
        return BuildMessage(messageType, options, controlField, body);
    }

    private static byte[] BuildMessage(PtpMessageType messageType, PtpBuildOptions options, byte controlField, ReadOnlySpan<byte> body)
    {
        var bytes = new byte[PtpConstants.HeaderLength + body.Length];
        var span = bytes.AsSpan();
        span[0] = (byte)((options.TransportSpecific << 4) | ((byte)messageType & 0x0F));
        span[1] = PtpConstants.Version2;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), (ushort)bytes.Length);
        span[4] = options.DomainNumber;
        var flags = options.TwoStepFlag ? (ushort)0x0200 : (ushort)0;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), flags);
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(8, 8), options.CorrectionField);
        options.SourcePortIdentity.ClockIdentity.CopyTo(span.Slice(20, 8));
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(28, 2), options.SourcePortIdentity.PortNumber);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(30, 2), options.SequenceId);
        span[32] = controlField;
        span[33] = unchecked((byte)options.LogMessageInterval);
        body.CopyTo(span[PtpConstants.HeaderLength..]);
        return bytes;
    }
}
