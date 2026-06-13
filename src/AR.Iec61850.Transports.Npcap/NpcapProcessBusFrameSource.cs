using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AR.Iec61850.Transports;
using SharpPcap;

namespace AR.Iec61850.Transports.Npcap;

public sealed class NpcapProcessBusFrameSource : IProcessBusFrameSource, IDisposable
{
    private readonly ICaptureDevice _device;
    private readonly object _gate = new();
    private bool _capturing;
    private bool _disposed;

    public NpcapProcessBusFrameSource(string adapterSelector)
        : this(NpcapAdapterCatalog.ResolveAdapter(adapterSelector))
    {
    }

    public NpcapProcessBusFrameSource(ICaptureDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public async IAsyncEnumerable<ProcessBusCapturedFrame> CaptureAsync(
        ProcessBusCaptureOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new ProcessBusCaptureOptions();

        var channel = Channel.CreateBounded<ProcessBusCapturedFrame>(new BoundedChannelOptions(Math.Max(1, options.BufferCapacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        PacketArrivalEventHandler? handler = null;
        var started = false;
        using var registration = cancellationToken.Register(() => channel.Writer.TryComplete());

        try
        {
            lock (_gate)
            {
                if (_capturing)
                    throw new InvalidOperationException("This Npcap frame source is already capturing.");

                _capturing = true;
            }

            _device.Open(DeviceModes.Promiscuous, Math.Max(1, options.ReadTimeoutMilliseconds));
            if (!string.IsNullOrWhiteSpace(options.Filter) && _device is IPcapDevice pcapDevice)
                pcapDevice.Filter = options.Filter;

            handler = (_, capture) =>
            {
                var frame = new ProcessBusCapturedFrame
                {
                    Timestamp = ToDateTimeOffset(capture.Header.Timeval),
                    Frame = capture.Data.ToArray(),
                    Source = _device.Name ?? string.Empty
                };

                channel.Writer.TryWrite(frame);
            };

            _device.OnPacketArrival += handler;
            _device.StartCapture();
            started = true;

            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return frame;
        }
        finally
        {
            if (handler is not null)
                _device.OnPacketArrival -= handler;

            if (started)
            {
                try
                {
                    _device.StopCapture();
                }
                catch
                {
                    // Best-effort shutdown after cancellation or adapter removal.
                }
            }

            try
            {
                _device.Close();
            }
            catch
            {
                // Best-effort shutdown after cancellation or adapter removal.
            }

            lock (_gate)
                _capturing = false;

            channel.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _device.Close();
        }
        catch
        {
            // Best-effort cleanup only.
        }

        _disposed = true;
    }

    private static DateTimeOffset ToDateTimeOffset(PosixTimeval timeval)
    {
        var seconds = Convert.ToInt64(timeval.Seconds);
        var microseconds = Convert.ToInt64(timeval.MicroSeconds);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(checked(microseconds * 10));
    }
}
