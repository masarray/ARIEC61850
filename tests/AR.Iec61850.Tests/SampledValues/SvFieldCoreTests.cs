using System.IO.Compression;
using AR.Iec61850.SampledValues.Field;
using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvFieldCoreTests
{
    [Fact]
    public void ConfigurationWarningDoesNotMakeOperationalStreamBad()
    {
        var comparison = new SvConfigurationComparisonResult
        {
            Mode = SvComparisonMode.Compatible,
            Findings = [new SvConfigurationFinding(SvConfigurationFindingSeverity.Warning, "SV_CONFREV_MISMATCH", "confRev", "100", "200", "Configuration revision differs.")]
        };
        var report = SvFieldHealthEvaluator.Evaluate(new SvFieldHealthInput
        {
            RawFrameCount = 47_963,
            SvFrameCount = 47_938,
            ConfigurationComparison = comparison,
            IsSclBound = true,
            HasSemanticMapping = true,
            HasEngineeringScaling = true,
            Signal = new SvSignalAnalysis { State = SvSignalActivityState.NoiseDominated, Summary = "noise dominated" }
        });

        Assert.Equal(SvFieldHealthState.Good, report.OperationalState);
        Assert.Equal(SvFieldHealthState.Warning, report.Configuration.State);
        Assert.Equal(SvFieldHealthState.Quiet, report.Measurement.State);
    }

    [Fact]
    public void SignalAnalyzerLabelsNoiseDominatedWithoutForcingZero()
    {
        var random = new Random(61850);
        var samples = Enumerable.Range(0, 480)
            .Select(_ => random.NextDouble() * 2 - 1)
            .ToArray();

        var result = SvSignalStateAnalyzer.Analyze(samples, new SvSignalAnalysisOptions { SamplesPerCycle = 80 });

        Assert.Equal(SvSignalActivityState.NoiseDominated, result.State);
        Assert.True(result.AcRms > 0);
        Assert.NotEqual(0, result.PeakDeviation);
    }

    [Fact]
    public void SignalAnalyzerDetectsCoherentFundamental()
    {
        var samples = Enumerable.Range(0, 320)
            .Select(index => 100 * Math.Sin(2 * Math.PI * index / 80.0))
            .ToArray();

        var result = SvSignalStateAnalyzer.Analyze(samples, new SvSignalAnalysisOptions { SamplesPerCycle = 80 });

        Assert.Equal(SvSignalActivityState.Active, result.State);
        Assert.InRange(result.FundamentalRms!.Value, 70.70, 70.72);
    }

    [Fact]
    public void SclBindingDoesNotRequireOptionalDatasetField()
    {
        var result = SvSclBindingScorer.Score(
            new SvSclBindingCandidate
            {
                CandidateId = "AA1J1Q01A1/LLN0.MSVCB",
                ExpectedAppId = 0x4001,
                ExpectedDestinationMac = "01:0C:CD:04:00:00",
                ExpectedSvId = "AA1J1Q01A1MU01/LLN0.MSVCB",
                ExpectedDataSetReference = "AA1J1Q01A1/LLN0$MSV",
                ExpectedConfigurationRevision = 100,
                ExpectedAsduPerFrame = 1,
                ExpectedPayloadBytesPerAsdu = 64
            },
            new SvSclBindingObservation
            {
                AppId = 0x4001,
                DestinationMac = "01:0C:CD:04:00:00",
                SvId = "AA1J1Q01A1MU01/LLN0.MSVCB",
                DataSetReference = string.Empty,
                ConfigurationRevision = 200,
                AsduPerFrame = 1,
                PayloadBytesPerAsdu = 64
            });

        Assert.Equal(SvSclBindingConfidence.Confirmed, result.Confidence);
        Assert.Contains(result.Evidence, item => item.Field == "datSet" && item.Outcome == SvBindingEvidenceOutcome.Unknown);
        Assert.Contains(result.Evidence, item => item.Field == "confRev" && item.Outcome == SvBindingEvidenceOutcome.Conflict);
    }

    [Fact]
    public void KnownInjectionWithoutToleranceReturnsReview()
    {
        var result = SvKnownInjectionComparator.Compare(
            new SvKnownInjectionExpectation { Channel = "I01A", ExpectedRms = 0.1, Unit = "A", Domain = "secondary" },
            new SvKnownInjectionMeasurement { MeasuredRms = 0.0997 });

        Assert.Equal(SvKnownInjectionResultState.Review, result.State);
        Assert.InRange(result.AmplitudeErrorPercent!.Value, -0.31, -0.29);
    }

    [Fact]
    public void SupportBundleContainsManifestAndChecksums()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arsubsv-{Guid.NewGuid():N}.zip");
        try
        {
            SvSupportBundleWriter.Write(path,
                new SvSupportBundleManifest
                {
                    GeneratedAt = DateTimeOffset.UtcNow,
                    Application = "ArSubsv",
                    ApplicationVersion = "0.4.0",
                    EngineCommit = new string('a', 40),
                    PrivacyMode = SvSupportBundlePrivacyMode.MetadataOnly
                },
                [SvSupportBundleWriter.Text("diagnostics.md", "GOOD", "field diagnostics")]);

            using var archive = ZipFile.OpenRead(path);
            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("sha256sums.txt"));
            Assert.NotNull(archive.GetEntry("diagnostics.md"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
