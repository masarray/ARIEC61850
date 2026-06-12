namespace AR.Iec61850.Transports;

public sealed class InMemoryProcessBusTransport : IProcessBusTransport
{
    private readonly List<byte[]> _frames = new();

    public IReadOnlyList<byte[]> Frames => _frames;

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _frames.Add(frame.ToArray());
        return ValueTask.CompletedTask;
    }

    public void Clear()
        => _frames.Clear();
}
