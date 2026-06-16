
using AR.Iec61850.TimeSync.Ptp;
using AR.Iec61850.Transports;

namespace AR.Iec61850.TimeSync.PtpRuntime;

/// <summary>
/// Software PTPv2 Layer-2 publisher for isolated lab use. This runtime intentionally
/// does not claim hardware timestamping or certified grandmaster behavior.
/// </summary>
public sealed class PtpPublisherRuntime
{
    private readonly IProcessBusTransport _transport;
    private readonly PtpPublisherOptions _options;
    private readonly PtpSequenceCounters _sequences = new();
    private readonly object _sync = new();
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastSentAt;
    private long _announceSent;
    private long _syncSent;
    private long _followUpSent;
    private long _peerDelayResponsesSent;
    private bool _isRunning;
    private string _lastError = string.Empty;

    public PtpPublisherRuntime(IProcessBusTransport transport, PtpPublisherOptions options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.SourceMac.Length != 6)
            throw new ArgumentException("PTP source MAC must contain exactly 6 bytes.", nameof(options));
        if (_options.AnnounceInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Announce interval must be greater than zero.");
        if (_options.SyncInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Sync interval must be greater than zero.");
    }

    public PtpPublisherStatus GetStatus()
    {
        lock (_sync)
        {
            return new PtpPublisherStatus(_isRunning, _startedAt, _lastSentAt, _announceSent, _syncSent, _followUpSent, _peerDelayResponsesSent, _lastError);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        SetRunning(true);
        var nextAnnounce = DateTimeOffset.MinValue;
        var nextSync = DateTimeOffset.MinValue;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;

                if (now >= nextAnnounce)
                {
                    await SendAnnounceAsync(cancellationToken).ConfigureAwait(false);
                    nextAnnounce = now + _options.AnnounceInterval;
                }

                if (now >= nextSync)
                {
                    await SendSyncAsync(cancellationToken).ConfigureAwait(false);
                    nextSync = now + _options.SyncInterval;
                }

                var nextDue = nextAnnounce < nextSync ? nextAnnounce : nextSync;
                var delay = nextDue - DateTimeOffset.UtcNow;
                if (delay < TimeSpan.FromMilliseconds(2))
                    delay = TimeSpan.FromMilliseconds(2);
                if (delay > TimeSpan.FromMilliseconds(25))
                    delay = TimeSpan.FromMilliseconds(25);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            throw;
        }
        finally
        {
            SetRunning(false);
        }
    }

    public async ValueTask RespondToPeerDelayRequestAsync(PtpFrame request, CancellationToken cancellationToken = default)
    {
        if (!_options.RespondToPeerDelay || request.Header.MessageType != PtpMessageType.PdelayReq)
            return;

        var timestamp = PtpTimestamp.Now();
        var baseOptions = BuildOptions(PtpMessageType.PdelayResp, request.Header.SequenceId, timestamp) with
        {
            LogMessageInterval = request.Header.LogMessageInterval
        };

        var response = PtpMessageSerializer.BuildPdelayResp(baseOptions, request.Header.SourcePortIdentity);
        await SendFrameAsync(PtpConstants.PeerDelayMulticastMac.ToArray(), response, cancellationToken).ConfigureAwait(false);
        IncrementPeerDelayResponses();

        if (_options.TwoStepClock)
        {
            if (_options.FollowUpDelay > TimeSpan.Zero)
                await Task.Delay(_options.FollowUpDelay, cancellationToken).ConfigureAwait(false);

            var followUp = PtpMessageSerializer.BuildPdelayRespFollowUp(baseOptions with
            {
                SequenceId = request.Header.SequenceId,
                Timestamp = timestamp,
                TwoStepFlag = false
            }, request.Header.SourcePortIdentity);
            await SendFrameAsync(PtpConstants.PeerDelayMulticastMac.ToArray(), followUp, cancellationToken).ConfigureAwait(false);
            IncrementPeerDelayResponses();
        }
    }

    private async ValueTask SendAnnounceAsync(CancellationToken cancellationToken)
    {
        var options = BuildOptions(PtpMessageType.Announce, _sequences.Next(PtpMessageType.Announce), PtpTimestamp.Now()) with
        {
            LogMessageInterval = ToLogInterval(_options.AnnounceInterval),
            TwoStepFlag = false
        };

        var message = PtpMessageSerializer.BuildAnnounce(options, _options.CurrentUtcOffset);
        await SendFrameAsync(PtpConstants.GeneralMulticastMac.ToArray(), message, cancellationToken).ConfigureAwait(false);
        IncrementAnnounce();
    }

    private async ValueTask SendSyncAsync(CancellationToken cancellationToken)
    {
        var sequence = _sequences.Next(PtpMessageType.Sync);
        var timestamp = PtpTimestamp.Now();
        var options = BuildOptions(PtpMessageType.Sync, sequence, timestamp) with
        {
            LogMessageInterval = ToLogInterval(_options.SyncInterval),
            TwoStepFlag = _options.TwoStepClock
        };

        var sync = PtpMessageSerializer.BuildSync(options);
        await SendFrameAsync(PtpConstants.GeneralMulticastMac.ToArray(), sync, cancellationToken).ConfigureAwait(false);
        IncrementSync();

        if (!_options.TwoStepClock)
            return;

        if (_options.FollowUpDelay > TimeSpan.Zero)
            await Task.Delay(_options.FollowUpDelay, cancellationToken).ConfigureAwait(false);

        var followUp = PtpMessageSerializer.BuildFollowUp(options with
        {
            SequenceId = sequence,
            Timestamp = timestamp,
            TwoStepFlag = false
        });
        await SendFrameAsync(PtpConstants.GeneralMulticastMac.ToArray(), followUp, cancellationToken).ConfigureAwait(false);
        IncrementFollowUp();
    }

    private PtpBuildOptions BuildOptions(PtpMessageType messageType, ushort sequenceId, PtpTimestamp timestamp)
        => new()
        {
            DomainNumber = _options.DomainNumber,
            SourcePortIdentity = _options.SourcePortIdentity,
            SequenceId = sequenceId,
            Timestamp = timestamp,
            GrandmasterIdentity = _options.ClockIdentity,
            Priority1 = _options.Priority1,
            Priority2 = _options.Priority2,
            ClockClass = _options.ClockClass,
            ClockAccuracy = _options.ClockAccuracy,
            OffsetScaledLogVariance = _options.OffsetScaledLogVariance,
            TimeSource = _options.TimeSource,
            TwoStepFlag = _options.TwoStepClock,
            TransportSpecific = 0,
            LogMessageInterval = messageType == PtpMessageType.Announce
                ? ToLogInterval(_options.AnnounceInterval)
                : ToLogInterval(_options.SyncInterval)
        };

    private async ValueTask SendFrameAsync(byte[] destinationMac, byte[] ptpMessage, CancellationToken cancellationToken)
    {
        var frame = PtpMessageSerializer.BuildEthernetFrame(destinationMac, _options.SourceMac, ptpMessage, _options.VlanId, _options.VlanPriority);
        await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        lock (_sync)
            _lastSentAt = DateTimeOffset.UtcNow;
    }

    private void SetRunning(bool value)
    {
        lock (_sync)
        {
            _isRunning = value;
            if (value)
            {
                _startedAt = DateTimeOffset.UtcNow;
                _lastError = string.Empty;
            }
        }
    }

    private void SetError(string error)
    {
        lock (_sync)
            _lastError = error;
    }

    private void IncrementAnnounce()
    {
        lock (_sync)
            _announceSent++;
    }

    private void IncrementSync()
    {
        lock (_sync)
            _syncSent++;
    }

    private void IncrementFollowUp()
    {
        lock (_sync)
            _followUpSent++;
    }

    private void IncrementPeerDelayResponses()
    {
        lock (_sync)
        {
            _peerDelayResponsesSent++;
            _lastSentAt = DateTimeOffset.UtcNow;
        }
    }

    private static sbyte ToLogInterval(TimeSpan interval)
    {
        var seconds = Math.Max(interval.TotalSeconds, 1.0 / 128.0);
        var log2 = Math.Log(seconds, 2.0);
        return (sbyte)Math.Clamp((int)Math.Round(log2), -7, 7);
    }
}
