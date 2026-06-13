using System.Runtime.CompilerServices;

namespace AR.Iec61850.Transports;

public sealed class InMemoryProcessBusFrameSource : IProcessBusFrameSource
{
    private readonly IReadOnlyList<ProcessBusCapturedFrame> _frames;

    public InMemoryProcessBusFrameSource(IEnumerable<ProcessBusCapturedFrame> frames)
    {
        _frames = frames?.ToArray() ?? Array.Empty<ProcessBusCapturedFrame>();
    }

    public async IAsyncEnumerable<ProcessBusCapturedFrame> CaptureAsync(
        ProcessBusCaptureOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var frame in _frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
            await Task.Yield();
        }
    }
}
