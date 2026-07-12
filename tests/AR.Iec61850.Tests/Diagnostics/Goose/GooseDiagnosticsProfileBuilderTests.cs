using AR.Iec61850.Diagnostics.Goose;
using AR.Iec61850.Monitoring;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Diagnostics.Goose;

public sealed class GooseDiagnosticsProfileBuilderTests
{
    [Fact]
    public void Build_Classifies_Healthy_Goose_Stream()
    {
        var profile = LoadProfile();
        var expected = profile.ProcessBus.GooseStreams.Single();
        var summary = CreateSummary(expected);
        var now = DateTimeOffset.UtcNow;
        summary.RecordGoose(now, 1, 0, 1000, BuildValueDisplays(expected, false), Array.Empty<string>(), out _, out _);
        summary.RecordGoose(now.AddMilliseconds(100), 1, 1, 1000, BuildValueDisplays(expected, false), Array.Empty<string>(), out _, out _);

        var diagnostics = new GooseDiagnosticsProfileBuilder().Build(profile, [summary]);

        Assert.True(diagnostics.IsHealthy);
        Assert.Equal(1, diagnostics.ExpectedStreamCount);
        Assert.Equal(1, diagnostics.ObservedStreamCount);
        Assert.Equal(1, diagnostics.BoundStreamCount);
        Assert.Equal(1, diagnostics.HealthyStreamCount);
        Assert.Equal(GooseDiagnosticsStreamStatus.Healthy, diagnostics.Streams[0].Status);
        Assert.DoesNotContain(diagnostics.Findings, f => f.Severity == "High");
    }

    [Fact]
    public void Build_Flags_Missing_Expected_Goose_Stream()
    {
        var diagnostics = new GooseDiagnosticsProfileBuilder().Build(LoadProfile(), Array.Empty<ProcessBusStreamSummary>());

        Assert.False(diagnostics.IsHealthy);
        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_EXPECTED_MISSING" && f.Severity == "High");
        Assert.Equal(GooseDiagnosticsStreamStatus.Missing, diagnostics.Streams.Single().Status);
    }

    [Fact]
    public void Build_Flags_Sequence_Regression_Timeout_And_Test_Flag()
    {
        var profile = LoadProfile();
        var expected = profile.ProcessBus.GooseStreams.Single();
        var summary = CreateSummary(expected);
        var now = DateTimeOffset.UtcNow;

        summary.RecordGoose(now, 2, 0, 100, BuildValueDisplays(expected, false), Array.Empty<string>(), out _, out _);
        summary.RecordGoose(now.AddMilliseconds(50), 2, 1, 100, BuildValueDisplays(expected, false), Array.Empty<string>(), out _, out _);
        summary.RecordGoose(now.AddMilliseconds(100), 2, 4, 100, BuildValueDisplays(expected, false), Array.Empty<string>(), out _, out _);
        summary.RecordGoose(now.AddMilliseconds(500), 2, 5, 100, BuildValueDisplays(expected, true), ["GOOSE test flag is set.", "GOOSE values changed without a state-number increment."], out _, out _);
        summary.SetLastDiagnostics(["GOOSE test flag is set.", "GOOSE values changed without a state-number increment."]);
        summary.RecordGoose(now.AddMilliseconds(550), 1, 0, 100, BuildValueDisplays(expected, true), ["GOOSE ndsCom flag is set."], out _, out _);
        summary.SetLastDiagnostics(["GOOSE test flag is set.", "GOOSE ndsCom flag is set.", "GOOSE values changed without a state-number increment."]);

        var diagnostics = new GooseDiagnosticsProfileBuilder().Build(profile, [summary]);

        Assert.False(diagnostics.IsHealthy);
        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_SEQUENCE_GAP");
        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_STATE_OR_SEQUENCE_REGRESSION" && f.Severity == "High");
        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_SUPERVISION_TIMEOUT");
        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_TEST_FLAG_SET");
        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_NDSCOM_SET");
        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_VALUE_CHANGE_WITHOUT_STATE_INCREMENT" && f.Severity == "High");
    }

    [Fact]
    public void Build_Flags_Unexpected_Observed_Goose_Stream()
    {
        var profile = LoadProfile();
        var unexpected = new ProcessBusStreamSummary
        {
            Kind = ProcessBusEventKind.Goose,
            AppId = 0x9999,
            Source = "02:00:00:00:99:99",
            Destination = "01:0C:CD:04:99:99",
            VlanId = 999,
            VlanPriority = 4,
            StreamId = "UNEXPECTED/LLN0$GO$GCB99",
            ConfigurationRevision = 1
        };
        unexpected.RecordGoose(DateTimeOffset.UtcNow, 1, 0, 1000, ["true"], Array.Empty<string>(), out _, out _);

        var diagnostics = new GooseDiagnosticsProfileBuilder().Build(profile, [unexpected]);

        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_EXPECTED_MISSING");
        Assert.Contains(diagnostics.Findings, f => f.Code == "GOOSE_UNEXPECTED_STREAM");
        Assert.Contains(diagnostics.Streams, s => s.Status == GooseDiagnosticsStreamStatus.Unexpected);
    }

    [Fact]
    public void Markdown_Includes_Goose_Diagnostic_Sections()
    {
        var diagnostics = new GooseDiagnosticsProfileBuilder().Build(LoadProfile(), Array.Empty<ProcessBusStreamSummary>(), "minimal-station.scd");

        var markdown = diagnostics.ToMarkdown();

        Assert.Contains("# GOOSE Diagnostics Profile", markdown);
        Assert.Contains("## Stream Matrix", markdown);
        Assert.Contains("## Findings", markdown);
        Assert.Contains("GOOSE_EXPECTED_MISSING", markdown);
    }

    private static SclEngineeringProfile LoadProfile()
        => new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());

    private static ProcessBusStreamSummary CreateSummary(SclGooseStream stream)
        => new()
        {
            Kind = ProcessBusEventKind.Goose,
            AppId = stream.Address.AppId ?? 0,
            Source = "02:00:00:00:00:01",
            Destination = NormalizeMac(stream.Address.DestinationMacText),
            VlanId = stream.Address.VlanId,
            VlanPriority = stream.Address.VlanPriority,
            StreamId = stream.ControlBlockReference,
            ConfigurationRevision = stream.ConfigurationRevision
        };

    private static IReadOnlyList<string> BuildValueDisplays(SclGooseStream stream, bool state)
        => Enumerable.Range(0, Math.Max(stream.Entries.Count, 1))
            .Select(index => state ? $"true-{index}" : $"false-{index}")
            .ToArray();

    private static string NormalizeMac(string text) => text.Replace('-', ':').ToUpperInvariant();
}
