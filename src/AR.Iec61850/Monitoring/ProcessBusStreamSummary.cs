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
    public int SequenceGapCount { get; private set; }
    public int MissedSampleCount { get; private set; }
    public int DuplicateSampleCount { get; private set; }
    public int OutOfOrderSampleCount { get; private set; }
    public int WrapCount { get; private set; }
    public int LastDecodedValueCount { get; private set; }
    public IReadOnlyList<string> LastDiagnostics { get; private set; } = Array.Empty<string>();
    public uint? LastStateNumber { get; private set; }
    public uint? LastSequenceNumber { get; private set; }

    public ProcessBusSequenceStatus RecordSample(
        ushort? sampleCount,
        ushort? sampleCounterWrap,
        int decodedValueCount,
        IReadOnlyList<string> diagnostics)
    {
        PacketCount++;
        LastDecodedValueCount = decodedValueCount;
        LastDiagnostics = diagnostics.ToArray();

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

    public void RecordGoose(uint? stateNumber, uint? sequenceNumber)
    {
        PacketCount++;

        if (stateNumber.HasValue)
            LastStateNumber = stateNumber;

        if (sequenceNumber.HasValue)
            LastSequenceNumber = sequenceNumber;
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
