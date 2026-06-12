using System.Buffers.Binary;

namespace AR.Iec61850.Capture;

public sealed class PcapWriter : IDisposable
{
    private const uint MagicNumberLittleEndian = 0xA1B2C3D4;
    private const ushort VersionMajor = 2;
    private const ushort VersionMinor = 4;
    private const uint LinkTypeEthernet = 1;
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private bool _disposed;

    public PcapWriter(Stream stream, uint snapLength = 65_535, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
        WriteGlobalHeader(snapLength);
    }

    public static void WriteAll(string filePath, IEnumerable<PcapPacket> packets, uint snapLength = 65_535)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(packets);

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(filePath);
        using var writer = new PcapWriter(stream, snapLength);

        foreach (var packet in packets)
            writer.WritePacket(packet.Timestamp, packet.Frame);
    }

    public void WritePacket(DateTimeOffset timestamp, ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frame.Length > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(frame));

        var unixSeconds = timestamp.ToUnixTimeSeconds();
        var micros = (timestamp - DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).Ticks / 10;

        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], checked((uint)unixSeconds));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), checked((uint)micros));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), checked((uint)frame.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12, 4), checked((uint)frame.Length));

        _stream.Write(header);
        _stream.Write(frame);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (!_leaveOpen)
            _stream.Dispose();

        _disposed = true;
    }

    private void WriteGlobalHeader(uint snapLength)
    {
        Span<byte> header = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], MagicNumberLittleEndian);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4, 2), VersionMajor);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6, 2), VersionMinor);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(8, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(16, 4), snapLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(20, 4), LinkTypeEthernet);
        _stream.Write(header);
    }
}
