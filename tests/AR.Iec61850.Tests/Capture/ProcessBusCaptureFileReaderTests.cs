using System.Buffers.Binary;
using AR.Iec61850.Capture;

namespace AR.Iec61850.Tests.Capture;

public sealed class ProcessBusCaptureFileReaderTests
{
    [Fact]
    public void ReadsClassicLittleEndianPcap()
    {
        var frame = new byte[] { 1, 2, 3, 4, 5, 6 };
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0xD4, 0xC3, 0xB2, 0xA1 });
        WriteUInt16(stream, 2); WriteUInt16(stream, 4);
        WriteUInt32(stream, 0); WriteUInt32(stream, 0); WriteUInt32(stream, 65535); WriteUInt32(stream, 1);
        WriteUInt32(stream, 10); WriteUInt32(stream, 250_000); WriteUInt32(stream, (uint)frame.Length); WriteUInt32(stream, (uint)frame.Length);
        stream.Write(frame); stream.Position = 0;

        var packet = Assert.Single(ProcessBusCaptureFileReader.Read(stream));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(10).AddMilliseconds(250), packet.Timestamp);
        Assert.Equal(frame, packet.Frame);
    }

    [Fact]
    public void ReadsLittleEndianPcapNgEnhancedPacket()
    {
        var frame = new byte[] { 0x01, 0x0C, 0xCD, 0x04, 0x00, 0x00, 1, 2, 3, 4, 5, 6, 0x88, 0xBA };
        using var stream = new MemoryStream();
        WriteSectionHeader(stream);
        WriteInterfaceDescription(stream);
        WriteEnhancedPacket(stream, frame, timestampMicroseconds: 1_500_000);
        stream.Position = 0;

        var packet = Assert.Single(ProcessBusCaptureFileReader.Read(stream));

        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1.5), packet.Timestamp);
        Assert.Equal(frame, packet.Frame);
    }

    private static void WriteSectionHeader(Stream stream)
    {
        WriteUInt32(stream, 0x0A0D0D0A); WriteUInt32(stream, 28); WriteUInt32(stream, 0x1A2B3C4D);
        WriteUInt16(stream, 1); WriteUInt16(stream, 0); WriteUInt64(stream, ulong.MaxValue); WriteUInt32(stream, 28);
    }

    private static void WriteInterfaceDescription(Stream stream)
    {
        WriteUInt32(stream, 1); WriteUInt32(stream, 20); WriteUInt16(stream, 1); WriteUInt16(stream, 0);
        WriteUInt32(stream, 65535); WriteUInt32(stream, 20);
    }

    private static void WriteEnhancedPacket(Stream stream, byte[] frame, ulong timestampMicroseconds)
    {
        var paddedLength = (frame.Length + 3) & ~3;
        var total = 32 + paddedLength;
        WriteUInt32(stream, 6); WriteUInt32(stream, (uint)total); WriteUInt32(stream, 0);
        WriteUInt32(stream, (uint)(timestampMicroseconds >> 32)); WriteUInt32(stream, (uint)timestampMicroseconds);
        WriteUInt32(stream, (uint)frame.Length); WriteUInt32(stream, (uint)frame.Length); stream.Write(frame);
        for (var index = frame.Length; index < paddedLength; index++) stream.WriteByte(0);
        WriteUInt32(stream, (uint)total);
    }

    private static void WriteUInt16(Stream stream, ushort value) { Span<byte> buffer = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(buffer, value); stream.Write(buffer); }
    private static void WriteUInt32(Stream stream, uint value) { Span<byte> buffer = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(buffer, value); stream.Write(buffer); }
    private static void WriteUInt64(Stream stream, ulong value) { Span<byte> buffer = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(buffer, value); stream.Write(buffer); }
}
