using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using AR.Iec61850.SampledValues.Analysis;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvSustainedStreamAnalyzerTests
{
    [Fact]
    public void SustainedAnalyzerTracksRateContinuityAndSynchronizationWithoutRetainingPayloadHistory()
    {
        var analyzer = new SvSustainedStreamAnalyzer(new SvSustainedAnalysisOptions
        {
            SampleCounterWrap = 4000,
            TimestampEvidence = SvCaptureTimestampEvidence.HostSoftwareTimestamp,
            StreamIdleTimeout = TimeSpan.FromMinutes(1)
        });
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        SvSustainedStreamSnapshot snapshot = new();
        for (var index = 0; index < 20; index++)
        {
            Assert.True(analyzer.TryObserve(
                start.AddMilliseconds(index * 0.25),
                BuildFrame((ushort)index, sampleSynchronization: 2),
                out snapshot));
        }

        Assert.Equal(20, snapshot.FrameCount);
        Assert.Equal(20, snapshot.AsduCount);
        Assert.Equal(19, snapshot.Continuity.ContinuousTransitions);
        Assert.Equal(0, snapshot.Continuity.GapTransitions);
        Assert.Equal(20, snapshot.Synchronization.SampleSynchronization2);
        Assert.Equal(4000.0, Assert.IsType<double>(snapshot.Timing.FrameRateHz), 3);
        Assert.Equal(4000.0, Assert.IsType<double>(snapshot.Timing.EstimatedSampleRateHz), 3);
        Assert.False(snapshot.Timing.IsStrongTimingEvidence);
        Assert.Contains("ordinary capture-arrival evidence", snapshot.Timing.ClaimBoundary, StringComparison.Ordinal);
        Assert.Equal(8, snapshot.StablePayloadBytesPerAsdu);
    }

    [Fact]
    public void SustainedAnalyzerCountsMissingSamplesAndDoesNotCallArrivalGapNetworkLoss()
    {
        var analyzer = new SvSustainedStreamAnalyzer(new SvSustainedAnalysisOptions
        {
            SampleCounterWrap = 4000,
            DropoutIntervalMultiplier = 2.0
        });
        var start = DateTimeOffset.UnixEpoch;

        analyzer.TryObserve(start, BuildFrame(10), out _);
        analyzer.TryObserve(start.AddMilliseconds(1), BuildFrame(11), out _);
        analyzer.TryObserve(start.AddMilliseconds(2), BuildFrame(14), out _);
        analyzer.TryObserve(start.AddMilliseconds(8), BuildFrame(15), out var snapshot);

        Assert.Equal(1, snapshot.Continuity.GapTransitions);
        Assert.Equal(2, snapshot.Continuity.MissingSamples);
        Assert.True(snapshot.Timing.DropoutIntervals >= 1);
        Assert.Contains(snapshot.Diagnostics, item => item.Contains("not proof of network loss", StringComparison.Ordinal));
    }

    [Fact]
    public void SustainedAnalyzerBoundsStreamRegistryAndEvictsOldestStream()
    {
        var analyzer = new SvSustainedStreamAnalyzer(new SvSustainedAnalysisOptions
        {
            MaximumStreams = 2,
            StreamIdleTimeout = TimeSpan.FromHours(1)
        });
        var start = DateTimeOffset.UnixEpoch;

        analyzer.TryObserve(start, BuildFrame(0, svId: "A", appId: 0x4001), out _);
        analyzer.TryObserve(start.AddMilliseconds(1), BuildFrame(0, svId: "B", appId: 0x4002), out _);
        analyzer.TryObserve(start.AddMilliseconds(2), BuildFrame(0, svId: "C", appId: 0x4003), out _);

        var snapshots = analyzer.SnapshotAll();
        Assert.Equal(2, snapshots.Count);
        Assert.DoesNotContain(snapshots, item => item.Key.SvId == "A");
        Assert.Contains(snapshots, item => item.Key.SvId == "B");
        Assert.Contains(snapshots, item => item.Key.SvId == "C");
    }

    [Fact]
    public void SustainedAnalyzerFlagsPayloadLayoutChangeInsideSameStreamIdentity()
    {
        var analyzer = new SvSustainedStreamAnalyzer();
        var start = DateTimeOffset.UnixEpoch;

        analyzer.TryObserve(start, BuildFrame(0, payloadBytes: 8), out _);
        analyzer.TryObserve(start.AddMilliseconds(1), BuildFrame(1, payloadBytes: 16), out var snapshot);

        Assert.True(snapshot.PayloadLayoutChanged);
        Assert.Equal(0, snapshot.StablePayloadBytesPerAsdu);
        Assert.Contains(snapshot.Diagnostics, item => item.Contains("payload length changed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HardwareTimestampLabelDoesNotOverclaimClockAccuracy()
    {
        var analyzer = new SvSustainedStreamAnalyzer(new SvSustainedAnalysisOptions
        {
            TimestampEvidence = SvCaptureTimestampEvidence.HardwareTimestamp
        });

        analyzer.TryObserve(DateTimeOffset.UnixEpoch, BuildFrame(0), out _);
        analyzer.TryObserve(DateTimeOffset.UnixEpoch.AddMilliseconds(1), BuildFrame(1), out var snapshot);

        Assert.True(snapshot.Timing.IsStrongTimingEvidence);
        Assert.Contains("require separate validation", snapshot.Timing.ClaimBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void SignalWindowAnalyzerReturnsRmsAndFundamentalPhasorForDeterministicSine()
    {
        const int samplesPerCycle = 80;
        const double peak = 100.0;
        const double phaseDegrees = 30.0;
        var phaseRadians = phaseDegrees * Math.PI / 180.0;
        var samples = Enumerable.Range(0, samplesPerCycle)
            .Select(index => peak * Math.Cos((2.0 * Math.PI * index / samplesPerCycle) + phaseRadians))
            .ToArray();

        var result = SvSignalWindowAnalyzer.AnalyzeFundamental(samples);

        var expectedRms = peak / Math.Sqrt(2.0);
        Assert.Equal(expectedRms, result.Rms, 9);
        Assert.Equal(expectedRms, result.MagnitudeRms, 9);
        Assert.Equal(phaseDegrees, result.AngleDegrees, 9);
        Assert.Equal(samplesPerCycle, result.SampleCount);
    }

    private static SampledValuesFrame BuildFrame(
        ushort sampleCount,
        byte sampleSynchronization = 2,
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
                        SampleSynchronization = sampleSynchronization,
                        SampleRate = 4000,
                        SampleMode = 1,
                        SamplePayload = new byte[payloadBytes]
                    }
                ]
            }
        };
}
