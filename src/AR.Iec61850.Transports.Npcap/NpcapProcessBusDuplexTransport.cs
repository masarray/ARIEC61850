using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AR.Iec61850.Transports;
using SharpPcap;

namespace AR.Iec61850.Transports.Npcap;

/// <summary>
/// Single-adapter process-bus session that can transmit SV/GOOSE frames while passively
/// monitoring process-bus traffic such as PTP on the same opened Npcap device.
/// </summary>
public sealed class NpcapProcessBusDuplexTransport : IProcessBusTransport, IProcessBusFrameSource, IDisposable
{
    private const ushort VlanEtherType = 0x8100;
    private const ushort SampledValuesEtherType = 0x88BA;
    private static readonly long MinimumLearnableIntervalTicks = Math.Max(1, Stopwatch.Frequency / 50_000); // 20 us
    private static readonly long MaximumLearnableIntervalTicks = Math.Max(1, Stopwatch.Frequency / 200); // 5 ms

    private readonly ICaptureDevice _device;
    private readonly IInjectionDevice _injectionDevice;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Dictionary<SvTransmitKey, SvTransmitClock> _svTransmitClocks = new();
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
            var isSampledValues = TryReadSampledValuesKey(frame.Span, out var streamKey);
            if (isSampledValues)
                await PaceSampledValuesAsync(streamKey, cancellationToken).ConfigureAwait(false);

            _injectionDevice.SendPacket(frame.ToArray());

            if (isSampledValues)
                CommitSampledValuesSend(streamKey, Stopwatch.GetTimestamp());
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

    private async ValueTask PaceSampledValuesAsync(SvTransmitKey key, CancellationToken cancellationToken)
    {
        if (!_svTransmitClocks.TryGetValue(key, out var clock) || clock.NominalIntervalTicks <= 0)
            return;

        var targetTicks = clock.LastSentTicks + clock.NominalIntervalTicks;
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

    private void CommitSampledValuesSend(SvTransmitKey key, long sentTicks)
    {
        if (!_svTransmitClocks.TryGetValue(key, out var clock))
        {
            _svTransmitClocks[key] = new SvTransmitClock(sentTicks, 0);
            return;
        }

        var observedInterval = sentTicks - clock.LastSentTicks;
        var nominalInterval = clock.NominalIntervalTicks;
        var learnable = observedInterval >= MinimumLearnableIntervalTicks &&
                        observedInterval <= MaximumLearnableIntervalTicks &&
                        (nominalInterval <= 0 || observedInterval <= nominalInterval * 3);

        if (learnable)
        {
            nominalInterval = nominalInterval <= 0
                ? observedInterval
                : (long)Math.Round((nominalInterval * 0.9) + (observedInterval * 0.1));
        }

        _svTransmitClocks[key] = new SvTransmitClock(sentTicks, nominalInterval);
    }

    private static bool TryReadSampledValuesKey(ReadOnlySpan<byte> frame, out SvTransmitKey key)
    {
        key = default;
        if (frame.Length < 22)
            return false;

        var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));
        var processBusOffset = 14;
        ushort vlanId = 0;
        if (etherType == VlanEtherType)
        {
            if (frame.Length < 26)
                return false;

            vlanId = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(14, 2)) & 0x0FFF);
            etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(16, 2));
            processBusOffset = 18;
        }

        if (etherType != SampledValuesEtherType || frame.Length < processBusOffset + 2)
            return false;

        key = new SvTransmitKey(
            BinaryPrimitives.ReadUInt64BigEndian(frame.Slice(0, 8)),
            BinaryPrimitives.ReadUInt32BigEndian(frame.Slice(8, 4)),
            BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(processBusOffset, 2)),
            vlanId);
        return true;
    }

    private static DateTimeOffset ToDateTimeOffset(PosixTimeval timeval)
    {
        var seconds = Convert.ToInt64(timeval.Seconds);
        var microseconds = Convert.ToInt64(timeval.MicroSeconds);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(checked(microseconds * 10));
    }

    private readonly record struct SvTransmitKey(ulong MacPrefix, uint MacSuffix, ushort AppId, ushort VlanId);
    private sealed record SvTransmitClock(long LastSentTicks, long NominalIntervalTicks);
}
