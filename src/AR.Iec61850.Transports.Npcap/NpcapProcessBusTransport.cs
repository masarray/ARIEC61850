using AR.Iec61850.Transports;
using SharpPcap;

namespace AR.Iec61850.Transports.Npcap;

public sealed class NpcapProcessBusTransport : IProcessBusTransport, IDisposable
{
    private readonly ICaptureDevice _device;
    private readonly IInjectionDevice _injectionDevice;
    private bool _disposed;

    public NpcapProcessBusTransport(string adapterSelector)
        : this(NpcapAdapterCatalog.ResolveAdapter(adapterSelector))
    {
    }

    public NpcapProcessBusTransport(ICaptureDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _injectionDevice = device as IInjectionDevice
            ?? throw new InvalidOperationException("The selected adapter does not support packet injection.");
        _device.Open(DeviceModes.Promiscuous, 1000);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _injectionDevice.SendPacket(frame.ToArray());
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _device.Close();
        _disposed = true;
    }
}
