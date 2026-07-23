using AR.Iec61850.Capture;

namespace AR.Iec61850.SampledValues;

public static class SampledValuesPcapExporter
{
    public static void WriteGeneratedFrames(string filePath, IEnumerable<(DateTimeOffset Timestamp, byte[] Frame)> frames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(frames);
        PcapWriter.WriteAll(filePath, frames.Select(frame => new PcapPacket(frame.Timestamp, frame.Frame)));
    }
}
