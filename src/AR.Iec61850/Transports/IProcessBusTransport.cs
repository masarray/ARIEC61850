namespace AR.Iec61850.Transports;

public interface IProcessBusTransport
{
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);
}
