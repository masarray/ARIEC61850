using AR.Iec61850.Diagnostics.SampledValues;
using AR.Iec61850.Monitoring;
using AR.Iec61850.SampledValues;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Diagnostics.SampledValues;

public sealed class SampledValuesDiagnosticsProfileBuilderTests
{
    [Fact]
    public void Build_Classifies_Healthy_Sampled_Values_Stream()
    {
        var profile = LoadProfile();
        var expected = profile.ProcessBus.SampledValuesStreams.Single();
        var summary = CreateSummary(expected);
        var payloadBytes = ExpectedPayloadBytes(expected);
        var mode = MapSampleMode(expected.SampleMode);

        summary.RecordSample(0, 4000, expected.Entries.Count, Array.Empty<string>(), payloadBytes, expected.SampleRate, mode, 2, expected.NoAsdu);
        summary.RecordSample(1, 4000, expected.Entries.Count, Array.Empty<string>(), payloadBytes, expected.SampleRate, mode, 2, expected.NoAsdu);

        var diagnostics = new SampledValuesDiagnosticsProfileBuilder().Build(profile, [summary]);

        Assert.True(diagnostics.IsHealthy);
        Assert.Equal(1, diagnostics.ExpectedStreamCount);
        Assert.Equal(1, diagnostics.ObservedStreamCount);
        Assert.Equal(1, diagnostics.BoundStreamCount);
        Assert.Equal(1, diagnostics.HealthyStreamCount);
        Assert.Equal(SampledValuesDiagnosticsStreamStatus.Healthy, diagnostics.Streams[0].Status);
        Assert.Empty(diagnostics.Findings.Where(f => f.Severity == "High"));
    }

    [Fact]
    public void Build_Flags_Missing_Expected_Sampled_Values_Stream()
    {
        var diagnostics = new SampledValuesDiagnosticsProfileBuilder().Build(LoadProfile(), Array.Empty<ProcessBusStreamSummary>());

        Assert.False(diagnostics.IsHealthy);
        Assert.Contains(diagnostics.Findings, f => f.Code == "SV_EXPECTED_MISSING" && f.Severity == "High");
        Assert.Equal(SampledValuesDiagnosticsStreamStatus.Missing, diagnostics.Streams.Single().Status);
    }

    [Fact]
    public void Build_Flags_Sequence_Sync_And_Payload_Anomalies()
    {
        var profile = LoadProfile();
        var expected = profile.ProcessBus.SampledValuesStreams.Single();
        var summary = CreateSummary(expected);
        var payloadBytes = ExpectedPayloadBytes(expected);
        var mode = MapSampleMode(expected.SampleMode);

        summary.RecordSample(10, 4000, expected.Entries.Count, Array.Empty<string>(), payloadBytes, expected.SampleRate, mode, 2, expected.NoAsdu);
        summary.RecordSample(11, 4000, expected.Entries.Count, Array.Empty<string>(), payloadBytes, expected.SampleRate, mode, 2, expected.NoAsdu);
        summary.RecordSample(14, 4000, expected.Entries.Count, Array.Empty<string>(), payloadBytes, expected.SampleRate, mode, 2, expected.NoAsdu);
        summary.RecordSample(14, 4000, expected.Entries.Count, Array.Empty<string>(), payloadBytes, expected.SampleRate, mode, 2, expected.NoAsdu);
        summary.RecordSample(13, 4000, expected.Entries.Count, Array.Empty<string>(), payloadBytes, expected.SampleRate, mode, 2, expected.NoAsdu);
        summary.RecordSample(15, 4000, Math.Max(0, expected.Entries.Count - 1),
            ["SV smpSynch is 0; expected synchronized value 2 for normal process-bus evidence.", "SV payload is too short. Expected at least 16 byte(s), got 12."],
            Math.Max(0, payloadBytes - 4), expected.SampleRate, mode, 0, expected.NoAsdu);

        var diagnostics = new SampledValuesDiagnosticsProfileBuilder().Build(profile, [summary]);

        Assert.False(diagnostics.IsHealthy);
        Assert.Contains(diagnostics.Findings, f => f.Code == "SV_SAMPLE_COUNT_GAP");
        Assert.Contains(diagnostics.Findings, f => f.Code == "SV_DUPLICATE_SAMPLE_COUNT");
        Assert.Contains(diagnostics.Findings, f => f.Code == "SV_OUT_OF_ORDER_SAMPLE_COUNT" && f.Severity == "High");
        Assert.Contains(diagnostics.Findings, f => f.Code == "SV_SAMPLE_SYNCHRONIZATION_ISSUE");
        Assert.Contains(diagnostics.Findings, f => f.Code == "SV_PAYLOAD_LENGTH_MISMATCH" && f.Severity == "High");
    }

    [Fact]
    public void Build_Flags_Unexpected_Observed_Sampled_Values_Stream()
    {
        var profile = LoadProfile();
        var unexpected = new ProcessBusStreamSummary
        {
            Kind = ProcessBusEventKind.SampledValues,
            AppId = 0x9999,
            Source = "02:00:00:00:99:99",
            Destination = "01:0C:CD:04:99:99",
            VlanId = 999,
            VlanPriority = 4,
            StreamId = "unexpected-sv",
            ConfigurationRevision = 1
        };
        unexpected.RecordSample(0, 4000, 1, Array.Empty<string>(), 4, 4000, 0, 2, 1);

        var diagnostics = new SampledValuesDiagnosticsProfileBuilder().Build(profile, [unexpected]);

        Assert.Contains(diagnostics.Findings, f => f.Code == "SV_EXPECTED_MISSING");
        Assert.Contains(diagnostics.Findings, f => f.Code == "SV_UNEXPECTED_STREAM");
        Assert.Contains(diagnostics.Streams, s => s.Status == SampledValuesDiagnosticsStreamStatus.Unexpected);
    }

    [Fact]
    public void Markdown_Includes_Sampled_Values_Diagnostic_Sections()
    {
        var diagnostics = new SampledValuesDiagnosticsProfileBuilder().Build(LoadProfile(), Array.Empty<ProcessBusStreamSummary>(), "minimal-station.scd");

        var markdown = diagnostics.ToMarkdown();

        Assert.Contains("# Sampled Values Diagnostics Profile", markdown);
        Assert.Contains("## Stream Matrix", markdown);
        Assert.Contains("## Findings", markdown);
        Assert.Contains("SV_EXPECTED_MISSING", markdown);
    }

    private static SclEngineeringProfile LoadProfile()
        => new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());

    private static ProcessBusStreamSummary CreateSummary(SclSampledValuesStream stream)
        => new()
        {
            Kind = ProcessBusEventKind.SampledValues,
            AppId = stream.Address.AppId ?? 0,
            Source = "02:00:00:00:00:01",
            Destination = NormalizeMac(stream.Address.DestinationMacText),
            VlanId = stream.Address.VlanId,
            VlanPriority = stream.Address.VlanPriority,
            StreamId = stream.SvId,
            ConfigurationRevision = stream.ConfigurationRevision
        };

    private static int ExpectedPayloadBytes(SclSampledValuesStream stream)
        => SampledValuesPayloadLayout.FromDataSet(stream.Entries).PayloadByteLength;

    private static ushort? MapSampleMode(string sampleMode)
        => sampleMode.Trim() switch
        {
            "SmpPerPeriod" => 0,
            "SmpPerSec" => 1,
            "SecPerSmp" => 2,
            _ => null
        };

    private static string NormalizeMac(string text) => text.Replace('-', ':').ToUpperInvariant();
}
