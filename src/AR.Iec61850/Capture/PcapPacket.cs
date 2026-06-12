namespace AR.Iec61850.Capture;

public sealed record PcapPacket(DateTimeOffset Timestamp, byte[] Frame);
