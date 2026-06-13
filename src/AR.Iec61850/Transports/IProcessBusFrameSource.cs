namespace AR.Iec61850.Transports;

public interface IProcessBusFrameSource
{
    IAsyncEnumerable<ProcessBusCapturedFrame> CaptureAsync(
        ProcessBusCaptureOptions options,
        CancellationToken cancellationToken = default);
}
