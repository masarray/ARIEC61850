namespace AR.Iec61850.Monitoring;

public sealed class ProcessBusStreamSummary
{
    public ProcessBusEventKind Kind { get; init; }
    public ushort AppId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public ushort? VlanId { get; init; }
    public byte? VlanPriority { get; init; }
    public string StreamId { get; init; } = string.Empty;
    public uint? ConfigurationRevision { get; init; }
    public int PacketCount { get; private set; }
    public ushort? FirstSampleCount { get; private set; }
    public ushort? LastSampleCount { get; private set; }
    public ushort? ExpectedNextSampleCount { get; private set; }
    public ushort? SampleCounterWrap { get; private set; }
    public ushort? LastSampleRate { get; private set; }
    public ushort? LastSampleMode { get; private set; }
    public byte? LastSampleSynchronization { get; private set; }
    public int LastAsduCount { get; private set; }
    public int LastPayloadBytes { get; private set; }
    public int PayloadLengthChangeCount { get; private set; }
    public int SampleSynchronizationIssueCount { get; private set; }
    public int SequenceGapCount { get; private set; }
    public int MissedSampleCount { get; private set; }
    public int DuplicateSampleCount { get; private set; }
    public int OutOfOrderSampleCount { get; private set; }
    public int WrapCount { get; private set; }
    public int LastDecodedValueCount { get; private set; }
    public IReadOnlyList<string> LastDiagnostics { get; private set; } = Array.Empty<string>();
    public uint? LastStateNumber { get; private set; }
    public uint? LastSequenceNumber { get; private set; }
    public uint? LastTimeAllowedToLiveMilliseconds { get; private set; }
    public double? LastArrivalGapMilliseconds { get; private set; }
    public double? MaxArrivalGapMilliseconds { get; private set; }
    public DateTimeOffset? LastGooseTimestamp => _lastGooseTimestamp;
    public IReadOnlyList<string> LastGooseValueDisplays => _lastGooseValueDisplays;
    public int GooseStateChangeCount { get; private set; }
    public int GooseRetransmissionCount { get; private set; }
    public int GooseSequenceGapCount { get; private set; }
    public int GooseDuplicateCount { get; private set; }
    public int GooseSequenceRegressionCount { get; private set; }
    public int GooseStateRegressionCount { get; private set; }
    public int GooseTimeoutCount { get; private set; }
    public int GooseValueChangeCount { get; private set; }
    public string LastChangedSummary { get; private set; } = string.Empty;
    private DateTimeOffset? _lastGooseTimestamp;
    private IReadOnlyList<string> _lastGooseValueDisplays = Array.Empty<string>();

    public ProcessBusSequenceStatus RecordSample(
        ushort? sampleCount,
        ushort? sampleCounterWrap,
        int decodedValueCount,
        IReadOnlyList<string> diagnostics,
        int payloadBytes = 0,
        ushort? sampleRate = null,
        ushort? sampleMode = null,
        byte? sampleSynchronization = null,
        int asduCount = 0)
    {
        PacketCount++;
        LastDecodedValueCount = decodedValueCount;
        LastDiagnostics = diagnostics.ToArray();
        LastSampleRate = sampleRate;
        LastSampleMode = sampleMode;
        LastSampleSynchronization = sampleSynchronization;
        LastAsduCount = asduCount;

        if (payloadBytes >= 0)
        {
            if (PacketCount > 1 && LastPayloadBytes > 0 && payloadBytes > 0 && payloadBytes != LastPayloadBytes)
                PayloadLengthChangeCount++;
            LastPayloadBytes = payloadBytes;
        }

        if (sampleSynchronization.HasValue && sampleSynchronization.Value != 2)
            SampleSynchronizationIssueCount++;

        if (sampleCounterWrap is > 0)
            SampleCounterWrap ??= sampleCounterWrap;

        if (!sampleCount.HasValue)
            return ProcessBusSequenceStatus.MissingSampleCount;

        if (!LastSampleCount.HasValue)
        {
            FirstSampleCount ??= sampleCount;
            LastSampleCount = sampleCount;
            ExpectedNextSampleCount = NextSampleCount(sampleCount.Value, SampleCounterWrap);
            return ProcessBusSequenceStatus.First;
        }

        var previous = LastSampleCount.Value;
        var expected = ExpectedNextSampleCount ?? NextSampleCount(previous, SampleCounterWrap);
        var actual = sampleCount.Value;

        if (actual == previous)
        {
            DuplicateSampleCount++;
            return ProcessBusSequenceStatus.Duplicate;
        }

        var status = ProcessBusSequenceStatus.InSequence;
        if (actual == expected)
        {
            if (actual < previous)
            {
                WrapCount++;
                status = ProcessBusSequenceStatus.Wrapped;
            }
        }
        else
        {
            var missedSamples = CountMissedSamples(expected, actual, SampleCounterWrap);
            if (IsLikelyForwardJump(missedSamples, SampleCounterWrap))
            {
                SequenceGapCount++;
                MissedSampleCount += missedSamples;
                status = ProcessBusSequenceStatus.Jump;
            }
            else
            {
                OutOfOrderSampleCount++;
                status = ProcessBusSequenceStatus.OutOfOrder;
            }
        }

        LastSampleCount = actual;
        ExpectedNextSampleCount = NextSampleCount(actual, SampleCounterWrap);
        return status;
    }

    public GooseSequenceStatus RecordGoose(
        DateTimeOffset timestamp,
        uint? stateNumber,
        uint? sequenceNumber,
        uint? timeAllowedToLiveMilliseconds,
        IReadOnlyList<string> valueDisplays,
        IReadOnlyList<string> diagnostics,
        out IReadOnlyList<bool> changedIndexes,
        out string changedSummary)
    {
        PacketCount++;
        LastDecodedValueCount = valueDisplays.Count;
        LastDiagnostics = diagnostics.ToArray();

        var mutableChangedIndexes = new bool[valueDisplays.Count];
        changedSummary = string.Empty;

        if (_lastGooseValueDisplays.Count > 0)
        {
            var changed = new List<string>();
            for (var i = 0; i < valueDisplays.Count; i++)
            {
                var previous = i < _lastGooseValueDisplays.Count ? _lastGooseValueDisplays[i] : string.Empty;
                if (!string.Equals(previous, valueDisplays[i], StringComparison.Ordinal))
                {
                    mutableChangedIndexes[i] = true;
                    changed.Add($"[{i}] {previous} -> {valueDisplays[i]}");
                }
            }

            GooseValueChangeCount += changed.Count;
            changedSummary = string.Join("; ", changed.Take(4));
            if (changed.Count > 4)
                changedSummary += $"; +{changed.Count - 4} more";
        }

        changedIndexes = mutableChangedIndexes;
        LastChangedSummary = changedSummary;

        if (_lastGooseTimestamp.HasValue)
        {
            var gap = (timestamp - _lastGooseTimestamp.Value).TotalMilliseconds;
            LastArrivalGapMilliseconds = gap;
            MaxArrivalGapMilliseconds = MaxArrivalGapMilliseconds.HasValue
                ? Math.Max(MaxArrivalGapMilliseconds.Value, gap)
                : gap;

            if (LastTimeAllowedToLiveMilliseconds is > 0 && gap > LastTimeAllowedToLiveMilliseconds.Value)
                GooseTimeoutCount++;
        }

        var status = ClassifyGoose(stateNumber, sequenceNumber);
        switch (status)
        {
            case GooseSequenceStatus.StateChange:
                GooseStateChangeCount++;
                break;
            case GooseSequenceStatus.Retransmission:
                GooseRetransmissionCount++;
                break;
            case GooseSequenceStatus.Duplicate:
                GooseDuplicateCount++;
                break;
            case GooseSequenceStatus.SequenceJump:
                GooseSequenceGapCount++;
                break;
            case GooseSequenceStatus.SequenceRegression:
                GooseSequenceRegressionCount++;
                break;
            case GooseSequenceStatus.StateRegression:
                GooseStateRegressionCount++;
                break;
        }

        LastTimeAllowedToLiveMilliseconds = timeAllowedToLiveMilliseconds;
        _lastGooseTimestamp = timestamp;

        if (stateNumber.HasValue)
            LastStateNumber = stateNumber;

        if (sequenceNumber.HasValue)
            LastSequenceNumber = sequenceNumber;

        _lastGooseValueDisplays = valueDisplays.ToArray();
        return status;
    }

    public void SetLastDiagnostics(IReadOnlyList<string> diagnostics)
        => LastDiagnostics = diagnostics.ToArray();

    private GooseSequenceStatus ClassifyGoose(uint? stateNumber, uint? sequenceNumber)
    {
        if (!stateNumber.HasValue || !sequenceNumber.HasValue)
            return GooseSequenceStatus.Unknown;

        if (!LastStateNumber.HasValue || !LastSequenceNumber.HasValue)
            return GooseSequenceStatus.First;

        var previousState = LastStateNumber.Value;
        var previousSequence = LastSequenceNumber.Value;
        var state = stateNumber.Value;
        var sequence = sequenceNumber.Value;

        if (state < previousState)
            return GooseSequenceStatus.StateRegression;

        if (state > previousState)
            return GooseSequenceStatus.StateChange;

        if (sequence == previousSequence)
            return GooseSequenceStatus.Duplicate;

        if (sequence < previousSequence)
            return GooseSequenceStatus.SequenceRegression;

        return sequence == previousSequence + 1
            ? GooseSequenceStatus.Retransmission
            : GooseSequenceStatus.SequenceJump;
    }

    private static ushort NextSampleCount(ushort current, ushort? sampleCounterWrap)
    {
        if (sampleCounterWrap is > 0)
            return (ushort)((current + 1) % sampleCounterWrap.Value);

        return current == ushort.MaxValue ? (ushort)0 : (ushort)(current + 1);
    }

    private static bool IsLikelyForwardJump(int missedSamples, ushort? sampleCounterWrap)
    {
        if (missedSamples <= 0)
            return false;

        var modulus = sampleCounterWrap is > 0 ? sampleCounterWrap.Value : ushort.MaxValue + 1;
        return missedSamples <= modulus / 2;
    }

    private static int CountMissedSamples(ushort expected, ushort actual, ushort? sampleCounterWrap)
    {
        var modulus = sampleCounterWrap is > 0 ? sampleCounterWrap.Value : ushort.MaxValue + 1;
        var delta = actual - expected;
        if (delta < 0)
            delta += modulus;

        return delta <= 0 ? 0 : delta;
    }
}
