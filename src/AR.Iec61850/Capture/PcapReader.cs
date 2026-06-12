using System.Buffers.Binary;

namespace AR.Iec61850.Capture;

public static class PcapReader
{
    private const uint MagicNumber = 0xA1B2C3D4;
    private const uint MagicNumberNanoseconds = 0xA1B23C4D;
    private const uint LinkTypeEthernet = 1;
    private const int GlobalHeaderLength = 24;
    private const int PacketHeaderLength = 16;

    public static IReadOnlyList<PcapPacket> ReadAll(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.OpenRead(filePath);
        return ReadAll(stream);
    }

    public static IReadOnlyList<PcapPacket> ReadAll(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> globalHeader = stackalloc byte[GlobalHeaderLength];
        if (!TryReadExactly(stream, globalHeader))
            throw new PcapFormatException("PCAP file is shorter than the global header.");

        var byteOrder = ResolveByteOrder(globalHeader[..4], out var timestampResolution);
        var versionMajor = ReadUInt16(globalHeader.Slice(4, 2), byteOrder);
        var versionMinor = ReadUInt16(globalHeader.Slice(6, 2), byteOrder);
        var linkType = ReadUInt32(globalHeader.Slice(20, 4), byteOrder);

        if (versionMajor != 2 || versionMinor != 4)
            throw new PcapFormatException($"Unsupported PCAP version {versionMajor}.{versionMinor}.");

        if (linkType != LinkTypeEthernet)
            throw new PcapFormatException($"Unsupported PCAP link type {linkType}; only Ethernet is supported.");

        var packets = new List<PcapPacket>();
        Span<byte> packetHeader = stackalloc byte[PacketHeaderLength];

        while (true)
        {
            var bytesRead = ReadSome(stream, packetHeader);
            if (bytesRead == 0)
                break;

            if (bytesRead != PacketHeaderLength)
                throw new PcapFormatException("Truncated PCAP packet header.");

            var seconds = ReadUInt32(packetHeader[..4], byteOrder);
            var fractional = ReadUInt32(packetHeader.Slice(4, 4), byteOrder);
            var includedLength = ReadUInt32(packetHeader.Slice(8, 4), byteOrder);

            if (includedLength > int.MaxValue)
                throw new PcapFormatException($"PCAP packet is too large: {includedLength} bytes.");

            var frame = new byte[includedLength];
            if (!TryReadExactly(stream, frame))
                throw new PcapFormatException("Truncated PCAP packet payload.");

            packets.Add(new PcapPacket(ToTimestamp(seconds, fractional, timestampResolution), frame));
        }

        return packets;
    }

    private static PcapByteOrder ResolveByteOrder(ReadOnlySpan<byte> magicBytes, out PcapTimestampResolution timestampResolution)
    {
        var little = BinaryPrimitives.ReadUInt32LittleEndian(magicBytes);
        if (little == MagicNumber)
        {
            timestampResolution = PcapTimestampResolution.Microseconds;
            return PcapByteOrder.LittleEndian;
        }

        if (little == MagicNumberNanoseconds)
        {
            timestampResolution = PcapTimestampResolution.Nanoseconds;
            return PcapByteOrder.LittleEndian;
        }

        var big = BinaryPrimitives.ReadUInt32BigEndian(magicBytes);
        if (big == MagicNumber)
        {
            timestampResolution = PcapTimestampResolution.Microseconds;
            return PcapByteOrder.BigEndian;
        }

        if (big == MagicNumberNanoseconds)
        {
            timestampResolution = PcapTimestampResolution.Nanoseconds;
            return PcapByteOrder.BigEndian;
        }

        throw new PcapFormatException("Unsupported PCAP magic number.");
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, PcapByteOrder byteOrder)
        => byteOrder == PcapByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(source)
            : BinaryPrimitives.ReadUInt16BigEndian(source);

    private static uint ReadUInt32(ReadOnlySpan<byte> source, PcapByteOrder byteOrder)
        => byteOrder == PcapByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source)
            : BinaryPrimitives.ReadUInt32BigEndian(source);

    private static DateTimeOffset ToTimestamp(uint seconds, uint fractional, PcapTimestampResolution timestampResolution)
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return timestampResolution == PcapTimestampResolution.Nanoseconds
            ? timestamp.AddTicks(fractional / 100)
            : timestamp.AddTicks(fractional * 10L);
    }

    private static bool TryReadExactly(Stream stream, Span<byte> destination)
        => ReadSome(stream, destination) == destination.Length;

    private static int ReadSome(Stream stream, Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = stream.Read(destination[total..]);
            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    private enum PcapByteOrder
    {
        LittleEndian,
        BigEndian
    }

    private enum PcapTimestampResolution
    {
        Microseconds,
        Nanoseconds
    }
}
