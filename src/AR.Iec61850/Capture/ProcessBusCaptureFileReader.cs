using System.Buffers.Binary;

namespace AR.Iec61850.Capture;

/// <summary>
/// Reads Ethernet packets from classic PCAP and PCAPNG capture files. The reader is deliberately
/// independent of Sampled Values decoding so live and offline traffic can share one protocol pipeline.
/// </summary>
public static class ProcessBusCaptureFileReader
{
    private const uint SectionHeaderBlock = 0x0A0D0D0A;
    private const uint InterfaceDescriptionBlock = 0x00000001;
    private const uint EnhancedPacketBlock = 0x00000006;
    private const uint SimplePacketBlock = 0x00000003;
    private const int MaximumPacketLength = 16 * 1024 * 1024;
    private const int MaximumBlockLength = 32 * 1024 * 1024;

    public static IEnumerable<PcapPacket> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        foreach (var packet in Read(stream))
            yield return packet;
    }

    public static IEnumerable<PcapPacket> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("Capture stream must be readable.", nameof(stream));

        var prefix = ReadExact(stream, 4, allowEndOfStream: false);
        stream.Position = stream.CanSeek
            ? stream.Position - prefix.Length
            : throw new ArgumentException("Capture stream must support seeking for format detection.", nameof(stream));

        if (prefix.AsSpan().SequenceEqual(new byte[] { 0x0A, 0x0D, 0x0D, 0x0A }))
        {
            foreach (var packet in ReadPcapNg(stream))
                yield return packet;
            yield break;
        }

        foreach (var packet in ReadClassicPcap(stream))
            yield return packet;
    }

    private static IEnumerable<PcapPacket> ReadClassicPcap(Stream stream)
    {
        var header = ReadExact(stream, 24, allowEndOfStream: false);
        var magic = header.AsSpan(0, 4);
        var littleEndian = magic.SequenceEqual(new byte[] { 0xD4, 0xC3, 0xB2, 0xA1 }) ||
                           magic.SequenceEqual(new byte[] { 0x4D, 0x3C, 0xB2, 0xA1 });
        var bigEndian = magic.SequenceEqual(new byte[] { 0xA1, 0xB2, 0xC3, 0xD4 }) ||
                        magic.SequenceEqual(new byte[] { 0xA1, 0xB2, 0x3C, 0x4D });
        if (!littleEndian && !bigEndian)
            throw new InvalidDataException("Capture is neither classic PCAP nor PCAPNG.");

        var nanosecondResolution = magic.SequenceEqual(new byte[] { 0x4D, 0x3C, 0xB2, 0xA1 }) ||
                                   magic.SequenceEqual(new byte[] { 0xA1, 0xB2, 0x3C, 0x4D });
        var linkType = ReadUInt32(header, 20, littleEndian);
        if (linkType != 1)
            throw new InvalidDataException($"Unsupported classic PCAP link type {linkType}; Ethernet link type 1 is required.");

        while (true)
        {
            var packetHeader = ReadExact(stream, 16, allowEndOfStream: true);
            if (packetHeader.Length == 0)
                yield break;
            if (packetHeader.Length != 16)
                throw new InvalidDataException("Classic PCAP packet header is truncated.");

            var seconds = ReadUInt32(packetHeader, 0, littleEndian);
            var fraction = ReadUInt32(packetHeader, 4, littleEndian);
            var includedLength = ReadUInt32(packetHeader, 8, littleEndian);
            if (includedLength == 0 || includedLength > MaximumPacketLength)
                throw new InvalidDataException($"Invalid PCAP packet length {includedLength}.");

            var frame = ReadExact(stream, checked((int)includedLength), allowEndOfStream: false);
            var ticks = nanosecondResolution ? fraction / 100.0 : fraction * 10.0;
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks((long)Math.Round(ticks));
            yield return new PcapPacket(timestamp, frame);
        }
    }

    private static IEnumerable<PcapPacket> ReadPcapNg(Stream stream)
    {
        var littleEndian = true;
        var sectionSeen = false;
        var interfaces = new List<PcapNgInterface>();

        while (true)
        {
            var header = ReadExact(stream, 8, allowEndOfStream: true);
            if (header.Length == 0)
                yield break;
            if (header.Length != 8)
                throw new InvalidDataException("PCAPNG block header is truncated.");

            var rawTypeLittle = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
            if (rawTypeLittle == SectionHeaderBlock)
            {
                var byteOrder = ReadExact(stream, 4, allowEndOfStream: false);
                littleEndian = byteOrder.AsSpan().SequenceEqual(new byte[] { 0x4D, 0x3C, 0x2B, 0x1A })
                    ? true
                    : byteOrder.AsSpan().SequenceEqual(new byte[] { 0x1A, 0x2B, 0x3C, 0x4D })
                        ? false
                        : throw new InvalidDataException("PCAPNG section byte-order magic is invalid.");

                var totalLength = ReadUInt32(header, 4, littleEndian);
                ValidateBlockLength(totalLength, 28);
                var remaining = ReadExact(stream, checked((int)totalLength - 12), allowEndOfStream: false);
                ValidateTrailingLength(remaining, totalLength, littleEndian);
                sectionSeen = true;
                interfaces.Clear();
                continue;
            }

            if (!sectionSeen)
                throw new InvalidDataException("PCAPNG does not begin with a Section Header Block.");

            var blockType = ReadUInt32(header, 0, littleEndian);
            var blockLength = ReadUInt32(header, 4, littleEndian);
            ValidateBlockLength(blockLength, 12);
            var blockRemainder = ReadExact(stream, checked((int)blockLength - 8), allowEndOfStream: false);
            ValidateTrailingLength(blockRemainder, blockLength, littleEndian);
            var body = blockRemainder.AsSpan(0, blockRemainder.Length - 4).ToArray();

            if (blockType == InterfaceDescriptionBlock)
            {
                interfaces.Add(ParseInterface(body, littleEndian));
                continue;
            }

            if (blockType == EnhancedPacketBlock)
            {
                var packet = ParseEnhancedPacket(body, littleEndian, interfaces);
                if (packet is not null)
                    yield return packet;
                continue;
            }

            if (blockType == SimplePacketBlock)
            {
                // Simple Packet Blocks do not carry a timestamp. Keep offline engineering evidence
                // deterministic by skipping them rather than inventing capture time.
                continue;
            }
        }
    }

    private static PcapNgInterface ParseInterface(byte[] body, bool littleEndian)
    {
        if (body.Length < 8)
            throw new InvalidDataException("PCAPNG Interface Description Block is truncated.");

        var linkType = ReadUInt16(body, 0, littleEndian);
        var resolution = 1e-6;
        var offset = 8;
        while (offset + 4 <= body.Length)
        {
            var code = ReadUInt16(body, offset, littleEndian);
            var length = ReadUInt16(body, offset + 2, littleEndian);
            offset += 4;
            if (code == 0)
                break;
            if (offset + length > body.Length)
                throw new InvalidDataException("PCAPNG interface option is truncated.");

            if (code == 9 && length >= 1)
            {
                var value = body[offset];
                resolution = (value & 0x80) == 0
                    ? Math.Pow(10, -(value & 0x7F))
                    : Math.Pow(2, -(value & 0x7F));
            }

            offset += Align4(length);
        }

        return new PcapNgInterface(linkType, resolution);
    }

    private static PcapPacket? ParseEnhancedPacket(byte[] body, bool littleEndian, IReadOnlyList<PcapNgInterface> interfaces)
    {
        if (body.Length < 20)
            throw new InvalidDataException("PCAPNG Enhanced Packet Block is truncated.");

        var interfaceId = ReadUInt32(body, 0, littleEndian);
        if (interfaceId >= interfaces.Count)
            throw new InvalidDataException($"PCAPNG packet references unknown interface {interfaceId}.");

        var captureInterface = interfaces[(int)interfaceId];
        if (captureInterface.LinkType != 1)
            return null;

        var timestampHigh = ReadUInt32(body, 4, littleEndian);
        var timestampLow = ReadUInt32(body, 8, littleEndian);
        var capturedLength = ReadUInt32(body, 12, littleEndian);
        if (capturedLength == 0 || capturedLength > MaximumPacketLength)
            throw new InvalidDataException($"Invalid PCAPNG packet length {capturedLength}.");
        if (20L + capturedLength > body.Length)
            throw new InvalidDataException("PCAPNG packet data is truncated.");

        var units = ((ulong)timestampHigh << 32) | timestampLow;
        var seconds = units * captureInterface.TimestampResolutionSeconds;
        var ticks = checked((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        var frame = body.AsSpan(20, checked((int)capturedLength)).ToArray();
        return new PcapPacket(DateTimeOffset.UnixEpoch.AddTicks(ticks), frame);
    }

    private static byte[] ReadExact(Stream stream, int length, bool allowEndOfStream)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(buffer, offset, length - offset);
            if (read == 0)
            {
                if (offset == 0 && allowEndOfStream)
                    return [];
                throw new InvalidDataException("Capture file is truncated.");
            }
            offset += read;
        }
        return buffer;
    }

    private static void ValidateBlockLength(uint length, int minimum)
    {
        if (length < minimum || length > MaximumBlockLength || length % 4 != 0)
            throw new InvalidDataException($"Invalid PCAPNG block length {length}.");
    }

    private static void ValidateTrailingLength(byte[] remainder, uint expected, bool littleEndian)
    {
        if (remainder.Length < 4)
            throw new InvalidDataException("PCAPNG block trailer is missing.");
        var trailing = ReadUInt32(remainder, remainder.Length - 4, littleEndian);
        if (trailing != expected)
            throw new InvalidDataException($"PCAPNG block length mismatch: header {expected}, trailer {trailing}.");
    }

    private static int Align4(int length) => (length + 3) & ~3;
    private static ushort ReadUInt16(byte[] source, int offset, bool littleEndian)
        => littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(offset, 2));
    private static uint ReadUInt32(byte[] source, int offset, bool littleEndian)
        => littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset, 4));

    private sealed record PcapNgInterface(ushort LinkType, double TimestampResolutionSeconds);
}
