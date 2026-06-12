using AR.Iec61850.Capture;
using System.Buffers.Binary;

namespace AR.Iec61850.Tests.Capture;

public sealed class PcapWriterTests
{
    [Fact]
    public void Writer_Produces_Classic_Pcap_With_Ethernet_LinkType()
    {
        using var stream = new MemoryStream();
        using (var writer = new PcapWriter(stream))
        {
            writer.WritePacket(
                new DateTimeOffset(2026, 6, 12, 12, 30, 0, 123, TimeSpan.Zero),
                Convert.FromHexString("010CCD01000102000000000188B80000"));
        }

        var bytes = stream.ToArray();

        Assert.True(bytes.Length > 24);
        Assert.Equal(0xA1B2C3D4U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)));
        Assert.Equal(65_535U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(1U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4)));
        Assert.Equal(16U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32, 4)));
        Assert.Equal(16U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(36, 4)));
    }
}
