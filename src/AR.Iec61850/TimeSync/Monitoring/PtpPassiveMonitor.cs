using AR.Iec61850.TimeSync.Ptp;

namespace AR.Iec61850.TimeSync.Monitoring;

/// <summary>
/// Passive in-memory PTP monitor for frames captured by a caller-provided transport.
/// The class intentionally has no SharpPcap/Npcap dependency so it can live in the core stack.
/// </summary>
public sealed class PtpPassiveMonitor
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SourceState> _sources = new(StringComparer.Ordinal);
    private readonly Queue<PtpObservedMessage> _recentMessages = new();
    private readonly int _recentCapacity;
    private int _totalFrames;
    private int _validFrames;
    private int _invalidFrames;

    public PtpPassiveMonitor(int recentCapacity = 256)
    {
        if (recentCapacity < 8)
            throw new ArgumentOutOfRangeException(nameof(recentCapacity), "Recent capacity must be at least 8.");

        _recentCapacity = recentCapacity;
    }

    public bool ObserveEthernetFrame(ReadOnlySpan<byte> ethernetFrame, DateTimeOffset? observedAt = null)
    {
        lock (_sync)
        {
            _totalFrames++;
        }

        if (!PtpPacketParser.TryParseEthernetFrame(ethernetFrame, out var frame))
        {
            lock (_sync)
            {
                _invalidFrames++;
            }

            return false;
        }

        Observe(frame, observedAt);
        return true;
    }

    public void Observe(PtpFrame frame, DateTimeOffset? observedAt = null)
    {
        var timestamp = observedAt ?? DateTimeOffset.UtcNow;
        var header = frame.Header;
        var observed = new PtpObservedMessage(
            timestamp,
            header.MessageType,
            header.DomainNumber,
            header.SourcePortIdentity,
            header.SequenceId,
            header.IsTwoStep,
            frame.VlanId,
            frame.OuterVlanId,
            frame.IsPeerDelayMulticast);

        lock (_sync)
        {
            _validFrames++;
            _recentMessages.Enqueue(observed);
            while (_recentMessages.Count > _recentCapacity)
                _recentMessages.Dequeue();

            var key = $"{header.DomainNumber}:{header.SourcePortIdentity}";
            if (!_sources.TryGetValue(key, out var state))
            {
                state = new SourceState(header.DomainNumber, header.SourcePortIdentity, timestamp);
                _sources[key] = state;
            }

            state.Observe(observed);
        }
    }

    public PtpMonitorSnapshot GetSnapshot(DateTimeOffset? capturedAt = null)
    {
        lock (_sync)
        {
            return new PtpMonitorSnapshot(
                capturedAt ?? DateTimeOffset.UtcNow,
                _totalFrames,
                _validFrames,
                _invalidFrames,
                _recentMessages.ToArray(),
                _sources.Values.Select(s => s.ToSnapshot()).OrderBy(s => s.DomainNumber).ThenBy(s => s.SourcePortIdentity.ToString()).ToArray());
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _totalFrames = 0;
            _validFrames = 0;
            _invalidFrames = 0;
            _sources.Clear();
            _recentMessages.Clear();
        }
    }

    private sealed class SourceState
    {
        private readonly Dictionary<PtpMessageType, int> _messageCounts = new();
        private readonly Dictionary<PtpMessageType, ushort> _lastSequenceIds = new();

        public SourceState(byte domainNumber, PtpPortIdentity sourcePortIdentity, DateTimeOffset firstSeenAt)
        {
            DomainNumber = domainNumber;
            SourcePortIdentity = sourcePortIdentity;
            FirstSeenAt = firstSeenAt;
            LastSeenAt = firstSeenAt;
        }

        public byte DomainNumber { get; }
        public PtpPortIdentity SourcePortIdentity { get; }
        public DateTimeOffset FirstSeenAt { get; }
        public DateTimeOffset LastSeenAt { get; private set; }
        public int SequenceAnomalyCount { get; private set; }
        public ushort? VlanId { get; private set; }
        public ushort? OuterVlanId { get; private set; }

        public void Observe(PtpObservedMessage message)
        {
            LastSeenAt = message.ObservedAt;
            VlanId = message.VlanId;
            OuterVlanId = message.OuterVlanId;
            _messageCounts[message.MessageType] = _messageCounts.TryGetValue(message.MessageType, out var count) ? count + 1 : 1;

            if (_lastSequenceIds.TryGetValue(message.MessageType, out var previous))
            {
                var expected = unchecked((ushort)(previous + 1));
                if (message.SequenceId != expected)
                    SequenceAnomalyCount++;
            }

            _lastSequenceIds[message.MessageType] = message.SequenceId;
        }

        public PtpSourceClockSnapshot ToSnapshot()
            => new(
                DomainNumber,
                SourcePortIdentity,
                FirstSeenAt,
                LastSeenAt,
                new Dictionary<PtpMessageType, int>(_messageCounts),
                new Dictionary<PtpMessageType, ushort>(_lastSequenceIds),
                SequenceAnomalyCount,
                VlanId,
                OuterVlanId);
    }
}
