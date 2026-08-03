using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Transports;
using SharpPcap;

namespace AR.Iec61850.Transports.Npcap;

/// <summary>
/// Single-adapter process-bus session that can transmit SV/GOOSE frames while passively
/// monitoring process-bus traffic such as PTP on the same opened Npcap device.
/// </summary>
public sealed class NpcapProcessBusDuplexTransport : IProcessBusTransport, IProcessBusFrameSource, IDisposable
{
    private readonly ICaptureDevice _device;
    private readonly IInjectionDevice _injectionDevice;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Dictionary<string, SvTransmitClock> _svTransmitClocks = new(StringComparer.Ordinal);
    private bool _capturing;
    private bool _disposed;

    public NpcapProcessBusDuplexTransport(string adapterSelector)
        : this(NpcapAdapterCatalog.ResolveAdapter(adapterSelector))
    {
    }

    public NpcapProcessBusDuplexTransport(ICaptureDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _injectionDevice = device as IInjectionDevice
            ?? throw new InvalidOperationException("The selected adapter does not support packet injection.");

        _device.Open(DeviceModes.Promiscuous, 1000);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PaceSampledValuesAsync(frame, cancellationToken).ConfigureAwait(false);
            _injectionDevice.SendPacket(frame.ToArray());
            CommitSampledValuesSend(frame, Stopwatch.GetTimestamp());
        }
        finally
        {
            _sendGate.Release();
        }
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
                    throw new InvalidOperationException("This Npcap session is already capturing.");

                _capturing = true;
            }

            if (!string.IsNullOrWhiteSpace(options.Filter) && _device is IPcapDevice pcapDevice)
                pcapDevice.Filter = options.Filter;

            handler = (_, capture) =>
            {
                var capturedFrame = new ProcessBusCapturedFrame
                {
                    Timestamp = ToDateTimeOffset(capture.Header.Timeval),
                    Frame = capture.Data.ToArray(),
                    Source = _device.Name ?? string.Empty
                };

                channel.Writer.TryWrite(capturedFrame);
            };

            _device.OnPacketArrival += handler;
            _device.StartCapture();
            started = true;

            await foreach (var capturedFrame in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return capturedFrame;
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

        _sendGate.Dispose();
        _disposed = true;
    }

    private async ValueTask PaceSampledValuesAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        if (!TryGetSampledValuesClock(frame, out var key, out var referenceTime))
            return;

        if (!_svTransmitClocks.TryGetValue(key, out var clock))
            return;

        var referenceInterval = referenceTime - clock.ReferenceTime;
        if (referenceInterval <= TimeSpan.Zero || referenceInterval > TimeSpan.FromMilliseconds(100))
            return;

        var intervalTicks = (long)Math.Round(referenceInterval.TotalSeconds * Stopwatch.Frequency);
        if (intervalTicks <= 0)
            return;

        var targetTicks = clock.SentTicks + intervalTicks;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingTicks = targetTicks - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
                return;

            var remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMilliseconds > 2)
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(Math.Min(remainingMilliseconds - 1, 10)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                Thread.SpinWait(64);
            }
        }
    }

    private void CommitSampledValuesSend(ReadOnlyMemory<byte> frame, long sentTicks)
    {
        if (!TryGetSampledValuesClock(frame, out var key, out var referenceTime))
            return;

        _svTransmitClocks[key] = new SvTransmitClock(referenceTime, sentTicks);
    }

    private static bool TryGetSampledValuesClock(
        ReadOnlyMemory<byte> frameBytes,
        out string key,
        out DateTimeOffset referenceTime)
    {
        key = string.Empty;
        referenceTime = default;

        if (!SampledValuesFrameParser.TryParseEthernetFrame(frameBytes, out var frame) ||
            frame.Pdu.Asdus.FirstOrDefault() is not { ReferenceTime: { } time } first)
        {
            return false;
        }

        referenceTime = time.Value;
        key = $"{frame.Source}|{frame.Destination}|{frame.Vlan?.VlanId.ToString() ?? "-"}|{frame.AppId:X4}|{first.SvId}";
        return true;
    }

    private static DateTimeOffset ToDateTimeOffset(PosixTimeval timeval)
    {
        var seconds = Convert.ToInt64(timeval.Seconds);
        var microseconds = Convert.ToInt64(timeval.MicroSeconds);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(checked(microseconds * 10));
    }

    private sealed record SvTransmitClock(DateTimeOffset ReferenceTime, long SentTicks);
}
