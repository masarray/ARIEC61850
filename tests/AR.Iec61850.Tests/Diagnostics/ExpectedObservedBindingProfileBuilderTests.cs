using AR.Iec61850.Diagnostics.Binding;
using AR.Iec61850.Monitoring;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Diagnostics;

public sealed class ExpectedObservedBindingProfileBuilderTests
{
    [Fact]
    public void Build_Binds_Expected_Goose_And_Sampled_Values_Streams()
    {
        var engineeringProfile = new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());
        var goose = engineeringProfile.ProcessBus.GooseStreams.Single();
        var sv = engineeringProfile.ProcessBus.SampledValuesStreams.Single();

        var summaries = new[]
        {
            CreateGooseSummary(goose),
            CreateSampledValuesSummary(sv)
        };

        var profile = new ExpectedObservedBindingProfileBuilder().Build(engineeringProfile, summaries);

        Assert.True(profile.IsReady);
        Assert.Equal(1, profile.ExpectedGooseCount);
        Assert.Equal(1, profile.BoundGooseCount);
        Assert.Equal(1, profile.ExpectedSampledValuesCount);
        Assert.Equal(1, profile.BoundSampledValuesCount);
        Assert.Equal(ProcessBusBindingMatchKind.Exact, profile.Goose[0].MatchKind);
        Assert.Equal(ProcessBusBindingMatchKind.Exact, profile.SampledValues[0].MatchKind);
        Assert.Empty(profile.UnexpectedObservedStreams);
        Assert.DoesNotContain(profile.Findings, f => f.Severity == "High");
    }

    [Fact]
    public void Build_Flags_Missing_Expected_Streams()
    {
        var engineeringProfile = new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());

        var profile = new ExpectedObservedBindingProfileBuilder().Build(engineeringProfile, Array.Empty<ProcessBusStreamSummary>());

        Assert.False(profile.IsReady);
        Assert.Contains(profile.Findings, f => f.Code == "PB_GOOSE_EXPECTED_MISSING" && f.Severity == "High");
        Assert.Contains(profile.Findings, f => f.Code == "PB_SV_EXPECTED_MISSING" && f.Severity == "High");
        Assert.Equal(2, profile.MissingExpectedCount);
    }

    [Fact]
    public void Build_Flags_Unexpected_Observed_Stream()
    {
        var engineeringProfile = new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());
        var goose = engineeringProfile.ProcessBus.GooseStreams.Single();
        var expected = CreateGooseSummary(goose);
        var unexpected = CreateUnexpectedGooseSummary();

        var profile = new ExpectedObservedBindingProfileBuilder().Build(engineeringProfile, new[] { expected, unexpected });

        Assert.Single(profile.UnexpectedObservedStreams);
        Assert.Contains(profile.Findings, f => f.Code == "PB_UNEXPECTED_OBSERVED_STREAM");
    }

    [Fact]
    public void Build_Flags_Partial_Binding_Mismatches()
    {
        var engineeringProfile = new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());
        var goose = engineeringProfile.ProcessBus.GooseStreams.Single();
        var observed = CreateGooseSummary(goose, appId: 0x7777, destination: "01:0C:CD:04:99:99", confRev: goose.ConfigurationRevision + 1U);

        var profile = new ExpectedObservedBindingProfileBuilder().Build(engineeringProfile, new[] { observed });

        Assert.Equal(ProcessBusBindingMatchKind.Partial, profile.Goose[0].MatchKind);
        Assert.Contains(profile.Findings, f => f.Code == "PB_GOOSE_APPID_MISMATCH");
        Assert.Contains(profile.Findings, f => f.Code == "PB_GOOSE_DESTINATION_MAC_MISMATCH");
        Assert.Contains(profile.Findings, f => f.Code == "PB_GOOSE_CONFREV_MISMATCH");
    }

    [Fact]
    public void Markdown_Includes_Binding_Sections()
    {
        var engineeringProfile = new SclEngineeringProfileBuilder().Load(SclParserTests.MinimalStationPath());
        var goose = engineeringProfile.ProcessBus.GooseStreams.Single();
        var profile = new ExpectedObservedBindingProfileBuilder().Build(engineeringProfile, new[] { CreateGooseSummary(goose) });

        var markdown = profile.ToMarkdown();

        Assert.Contains("# Expected vs Observed Process-Bus Binding", markdown);
        Assert.Contains("## GOOSE Binding", markdown);
        Assert.Contains("## Sampled Values Binding", markdown);
        Assert.Contains("## Findings", markdown);
    }

    private static ProcessBusStreamSummary CreateGooseSummary(SclGooseStream stream, ushort? appId = null, string? destination = null, uint? confRev = null)
    {
        var summary = new ProcessBusStreamSummary
        {
            Kind = ProcessBusEventKind.Goose,
            AppId = appId ?? stream.Address.AppId ?? 0,
            Source = "02:00:00:00:00:01",
            Destination = destination ?? NormalizeMac(stream.Address.DestinationMacText),
            VlanId = stream.Address.VlanId,
            VlanPriority = stream.Address.VlanPriority,
            StreamId = stream.ControlBlockReference,
            ConfigurationRevision = confRev ?? stream.ConfigurationRevision
        };

        summary.RecordGoose(
            DateTimeOffset.UtcNow,
            stateNumber: 1,
            sequenceNumber: 1,
            timeAllowedToLiveMilliseconds: stream.MaxTimeMilliseconds == 0 ? 1000u : stream.MaxTimeMilliseconds,
            valueDisplays: Enumerable.Range(0, Math.Max(stream.Entries.Count, 1)).Select(i => $"v{i}").ToArray(),
            diagnostics: Array.Empty<string>(),
            changedIndexes: out _,
            changedSummary: out _);
        return summary;
    }

    private static ProcessBusStreamSummary CreateSampledValuesSummary(SclSampledValuesStream stream)
    {
        var summary = new ProcessBusStreamSummary
        {
            Kind = ProcessBusEventKind.SampledValues,
            AppId = stream.Address.AppId ?? 0,
            Source = "02:00:00:00:00:02",
            Destination = NormalizeMac(stream.Address.DestinationMacText),
            VlanId = stream.Address.VlanId,
            VlanPriority = stream.Address.VlanPriority,
            StreamId = stream.SvId,
            ConfigurationRevision = stream.ConfigurationRevision
        };

        summary.RecordSample(sampleCount: 1, sampleCounterWrap: null, decodedValueCount: stream.Entries.Count, diagnostics: Array.Empty<string>());
        return summary;
    }

    private static ProcessBusStreamSummary CreateUnexpectedGooseSummary()
    {
        var summary = new ProcessBusStreamSummary
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

        summary.RecordGoose(DateTimeOffset.UtcNow, 1, 1, 1000, ["true"], Array.Empty<string>(), out _, out _);
        return summary;
    }

    private static string NormalizeMac(string text) => text.Replace('-', ':').ToUpperInvariant();
}
