namespace AR.Iec61850.Transports;

public sealed class ProcessBusCaptureOptions
{
    public string Filter { get; init; } = string.Empty;
    public int ReadTimeoutMilliseconds { get; init; } = 1000;
    public int BufferCapacity { get; init; } = 4096;
}
