using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using AR.Iec61850.SampledValues.Analysis;
using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvSustainedObservationSessionTests
{
    [Fact]
    public void SessionFeedsOneParsedFrameIntoObservationAndSustainedEvidence()
    {
        var session = new SvSustainedObservationSession();
        var frame = BuildFrame(17);

        Assert.True(session.TryObserve(
            DateTimeOffset.UnixEpoch,
            frame,
            SvObservationInputKind.LiveCapture,
            out var result));

        Assert.Equal(result.Observation.Key, result.Sustained.Key);
        Assert.Equal(SvObservedStreamKey.FromFrame(frame), result.Sustained.Key);
        Assert.Equal(1, result.Sustained.FrameCount);
        Assert.Equal(1, result.Sustained.AsduCount);
        Assert.Equal(1, session.ObservationStreamCount);
        Assert.Equal(1, session.SustainedStreamCount);
        Assert.Contains(SvObservationInputKind.LiveCapture, result.Observation.InputKinds);
    }

    [Fact]
    public void LiveCaptureAndPcapReplayProduceEquivalentSustainedMetrics()
    {
        var live = new SvSustainedObservationSession();
        var replay = new SvSustainedObservationSession();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        SvSustainedObservationResult liveResult = new();
        SvSustainedObservationResult replayResult = new();
        for (var index = 0; index < 64; index++)
        {
            var frame = BuildFrame((ushort)index);
            var timestamp = start.AddMilliseconds(index * 0.25);
            Assert.True(live.TryObserve(timestamp, frame, SvObservationInputKind.LiveCapture, out liveResult));
            Assert.True(replay.TryObserve(timestamp, frame, SvObservationInputKind.PcapReplay, out replayResult));
        }

        Assert.Equal(liveResult.Sustained.Key, replayResult.Sustained.Key);
        Assert.Equal(liveResult.Sustained.FrameCount, replayResult.Sustained.FrameCount);
        Assert.Equal(liveResult.Sustained.AsduCount, replayResult.Sustained.AsduCount);
        Assert.Equal(liveResult.Sustained.Continuity, replayResult.Sustained.Continuity);
        Assert.Equal(liveResult.Sustained.Synchronization, replayResult.Sustained.Synchronization);
        Assert.Equal(liveResult.Sustained.Timing.FrameRateHz, replayResult.Sustained.Timing.FrameRateHz);
        Assert.Equal(liveResult.Sustained.Timing.EstimatedSampleRateHz, replayResult.Sustained.Timing.EstimatedSampleRateHz);
        Assert.Contains(SvObservationInputKind.LiveCapture, liveResult.Observation.InputKinds);
        Assert.Contains(SvObservationInputKind.PcapReplay, replayResult.Observation.InputKinds);
    }

    [Fact]
    public void FrameRejectedByCanonicalObservationPathDoesNotAdvanceSustainedState()
    {
        var session = new SvSustainedObservationSession();
        var rejected = BuildFrame(0, payloadBytes: 0);

        Assert.False(session.TryObserve(
            DateTimeOffset.UnixEpoch,
            rejected,
            SvObservationInputKind.PcapReplay,
            out _));

        Assert.Equal(0, session.ObservationStreamCount);
        Assert.Equal(0, session.SustainedStreamCount);
        Assert.Empty(session.SnapshotObservations());
        Assert.Empty(session.SnapshotSustained());
    }

    [Fact]
    public void SessionPreservesBoundedSustainedRegistryDuringLongSyntheticIngestion()
    {
        var sustained = new SvSustainedStreamAnalyzer(new SvSustainedAnalysisOptions
        {
            MaximumStreams = 4,
            StreamIdleTimeout = TimeSpan.FromHours(1)
        });
        var session = new SvSustainedObservationSession(sustained: sustained);
        var start = DateTimeOffset.UnixEpoch;

        for (var index = 0; index < 10_000; index++)
        {
            var stream = index % 8;
            var frame = BuildFrame(
                (ushort)(index % 4000),
                svId: $"MU{stream:D2}",
                appId: (ushort)(0x4000 + stream));
            Assert.True(session.TryObserve(
                start.AddTicks(index * 2_500L),
                frame,
                SvObservationInputKind.PcapReplay,
                out _));
        }

        Assert.Equal(4, session.SustainedStreamCount);
        Assert.Equal(4, session.SnapshotSustained().Count);
        Assert.Equal(8, session.ObservationStreamCount);
    }

    [Fact]
    public void ClearResetsBothObservationLayersTogether()
    {
        var session = new SvSustainedObservationSession();
        Assert.True(session.TryObserve(
            DateTimeOffset.UnixEpoch,
            BuildFrame(0),
            SvObservationInputKind.LiveCapture,
            out _));

        session.Clear();

        Assert.Equal(0, session.ObservationStreamCount);
        Assert.Equal(0, session.SustainedStreamCount);
        Assert.Empty(session.SnapshotObservations());
        Assert.Empty(session.SnapshotSustained());
    }

    private static SampledValuesFrame BuildFrame(
        ushort sampleCount,
        string svId = "MU01",
        ushort appId = 0x4000,
        int payloadBytes = 8)
        => new()
        {
            Destination = MacAddress.Parse("01:0C:CD:04:00:01"),
            Source = MacAddress.Parse("02:00:00:00:00:01"),
            AppId = appId,
            Pdu = new SampledValuesPdu
            {
                Asdus =
                [
                    new SampledValueAsdu
                    {
                        SvId = svId,
                        DataSetReference = "MU01/LLN0$Dataset1",
                        SampleCount = sampleCount,
                        SampleSynchronization = 2,
                        SampleRate = 4000,
                        SampleMode = 1,
                        SamplePayload = new byte[payloadBytes]
                    }
                ]
            }
        };
}
