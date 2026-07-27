using System.Diagnostics;
using AR.Iec61850.SampledValues;
using AR.Iec61850.SampledValues.Measurements;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvPublisherSupportTests
{
    [Fact]
    public void PublisherQualityRoundTripsNetworkBitField()
    {
        var quality = new SampledValueQuality(
            SampledValueValidity.Questionable,
            Overflow: true,
            OldData: true,
            Test: true,
            OperatorBlocked: true);

        var restored = SampledValueQuality.FromUInt32(quality.ToUInt32());

        Assert.Equal(quality, restored);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x18, 0x87 }, quality.ToBytes());
    }

    [Theory]
    [InlineData(0, SvQualityValidity.Good)]
    [InlineData(1, SvQualityValidity.Invalid)]
    [InlineData(2, SvQualityValidity.Reserved)]
    [InlineData(3, SvQualityValidity.Questionable)]
    public void SemanticQualityDecoderUsesIecValidityEncoding(ushort word, SvQualityValidity expected)
        => Assert.Equal(expected, SvQualityDecoder.DecodeWord(word).Validity);

    [Fact]
    public void PublisherEvidenceWriterRecordsStreamAndSafetyBoundary()
    {
        var report = new SampledValuesPublisherEvidenceReport(
            ToolName: "Test Publisher",
            ToolVersion: "1.0.0",
            CreatedAt: new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero),
            SclPath: "station.scd",
            Adapter: "Lab NIC",
            Mode: "dry-run",
            TxTiming: "TX Timing: GOOD",
            SafetyBoundary: "TX-side evidence only",
            Streams:
            [
                new SampledValuesEvidenceStream(
                    SlotName: "Publisher 1",
                    IsEnabled: true,
                    ControlBlockReference: "IED1/LLN0.SMV1",
                    SvId: "MU01",
                    DataSetReference: "IED1/LLN0$DS1",
                    AppId: "0x4000",
                    SourceMac: "02:00:00:00:00:01",
                    DestinationMac: "01:0C:CD:04:00:01",
                    Vlan: "VID=1/PCP=4",
                    SampleRateHz: 4000,
                    PublicationRateHz: 4000,
                    NoAsdu: 1,
                    PayloadBytesPerAsdu: 64,
                    EstimatedEthernetBytes: 126,
                    EstimatedBandwidthBitsPerSecond: 4_032_000,
                    SignalSource: "Manual phasor",
                    Quality: "good",
                    SyncMode: "LocalCompatibility",
                    Status: "ready",
                    Findings: [])
            ],
            GlobalFindings: []);

        var markdown = SampledValuesPublisherEvidenceReportWriter.ToMarkdown(report);

        Assert.Contains("# Test Publisher Sampled Values Publisher Evidence Report", markdown, StringComparison.Ordinal);
        Assert.Contains("TX-side publisher evidence only", markdown, StringComparison.Ordinal);
        Assert.Contains("MU01", markdown, StringComparison.Ordinal);
        Assert.Contains("TX-side evidence only", markdown, StringComparison.Ordinal);
        Assert.Contains("4.032 Mbit/s", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void TxTimingHealthReportsGoodForOnScheduleFrames()
    {
        const double rate = 1000;
        var monitor = new TxTimingHealth(rate);
        var intervalTicks = (long)Math.Round(Stopwatch.Frequency / rate);
        var start = Stopwatch.GetTimestamp();

        for (var index = 0; index < 100; index++)
        {
            var scheduled = start + (index * intervalTicks);
            monitor.Record(scheduled, scheduled, scheduled + ToTicks(25));
        }

        var snapshot = monitor.Snapshot(start + (100 * intervalTicks));

        Assert.Equal(TxTimingHealthStatus.Good, snapshot.Status);
        Assert.Equal(100, snapshot.FrameCount);
        Assert.InRange(snapshot.ActualFramesPerSecond, 990, 1010);
        Assert.Equal(0, snapshot.MissedScheduleCount);
    }

    [Fact]
    public void TxTimingHealthReportsBadWhenOneIntervalIsMissedAfterWarmup()
    {
        const double rate = 1000;
        var monitor = new TxTimingHealth(rate);
        var intervalTicks = (long)Math.Round(Stopwatch.Frequency / rate);
        var start = Stopwatch.GetTimestamp();

        for (var index = 0; index < 8; index++)
        {
            var scheduled = start + (index * intervalTicks);
            monitor.Record(scheduled, scheduled, scheduled + ToTicks(25));
        }

        var missedSchedule = start + (8 * intervalTicks);
        monitor.Record(
            missedSchedule,
            missedSchedule + intervalTicks + ToTicks(100),
            missedSchedule + intervalTicks + ToTicks(150));
        var snapshot = monitor.Snapshot(start + (10 * intervalTicks));

        Assert.Equal(TxTimingHealthStatus.Bad, snapshot.Status);
        Assert.True(snapshot.MissedScheduleCount >= 1);
        Assert.True(snapshot.MaxLateByMicroseconds > 1000);
    }

    private static long ToTicks(double microseconds)
        => (long)Math.Round(microseconds * Stopwatch.Frequency / 1_000_000.0);
}
