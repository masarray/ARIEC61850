using AR.Iec61850.SampledValues.Analysis;
using AR.Iec61850.SampledValues.Measurements;
using AR.Iec61850.SampledValues.Profiles;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvEngineeringWindowAnalyzerTests
{
    [Fact]
    public void CompletesOneCycleRmsAndPhasorFromEvidenceBackedEngineeringSamples()
    {
        const int samplesPerCycle = 80;
        const double peak = 100.0;
        const double phaseDegrees = 30.0;
        var phaseRadians = phaseDegrees * Math.PI / 180.0;
        var analyzer = new SvEngineeringWindowAnalyzer();
        var key = CreateKey("MU01", 0x4000);
        var samples = Enumerable.Range(0, samplesPerCycle)
            .Select(index => EngineeringSample(
                (ushort)index,
                peak * Math.Cos((2.0 * Math.PI * index / samplesPerCycle) + phaseRadians)))
            .ToArray();

        var accepted = analyzer.TryObserve(
            Projection(key, samples),
            Evidence(4_000, 50),
            out var completed,
            out var reason);

        Assert.True(accepted, reason);
        var result = Assert.Single(completed);
        var expectedRms = peak / Math.Sqrt(2.0);
        Assert.Equal(expectedRms, result.Estimate.Rms, 9);
        Assert.Equal(expectedRms, result.Estimate.MagnitudeRms, 9);
        Assert.Equal(phaseDegrees, result.Estimate.AngleDegrees, 9);
        Assert.Equal(samplesPerCycle, result.SamplesPerCycle);
        Assert.Equal((ushort)0, result.FirstSampleCount);
        Assert.Equal((ushort)79, result.LastSampleCount);
        Assert.Equal("A", result.EngineeringUnit);
        Assert.Equal(SvEngineeringScaleSource.ManualOverride, result.ScaleSource);
        Assert.Equal(SvFactSource.TrustedContext, result.FundamentalFrequencySource);
    }

    [Fact]
    public void RawOnlyProjectionNeverCreatesEngineeringWindowState()
    {
        var analyzer = new SvEngineeringWindowAnalyzer();
        var raw = new SvProjectedMeasurementSample
        {
            SampleCount = 0,
            ElementIndex = 0,
            SignalReference = "MU01LD0/TCTR1.AmpSv.instMag.i",
            Cdc = "SAV",
            RawValue = 1234,
            Scale = SvEngineeringScale.RawOnly("test raw-only sample")
        };

        var accepted = analyzer.TryObserve(
            Projection(CreateKey(), raw),
            Evidence(4_000, 50),
            out var completed,
            out var reason);

        Assert.True(accepted, reason);
        Assert.Empty(completed);
        Assert.Equal(0, analyzer.ActiveStreamCount);
        Assert.Equal(0, analyzer.ActiveChannelCount);
        Assert.Contains("raw-only", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GapDiscardsPartialCycleAndRestartsAtCurrentSample()
    {
        var analyzer = new SvEngineeringWindowAnalyzer();
        var key = CreateKey();
        var evidence = Evidence(200, 50);

        Assert.True(analyzer.TryObserve(
            Projection(key, EngineeringSample(0, 1), EngineeringSample(1, 0)),
            evidence,
            out var first,
            out var firstReason), firstReason);
        Assert.Empty(first);

        Assert.True(analyzer.TryObserve(
            Projection(key, EngineeringSample(3, 0), EngineeringSample(4, 1)),
            evidence,
            out var second,
            out var secondReason), secondReason);
        Assert.Empty(second);

        Assert.True(analyzer.TryObserve(
            Projection(key, EngineeringSample(5, 0), EngineeringSample(6, -1)),
            evidence,
            out var completed,
            out var finalReason), finalReason);

        var result = Assert.Single(completed);
        Assert.Equal((ushort)3, result.FirstSampleCount);
        Assert.Equal((ushort)6, result.LastSampleCount);
        Assert.Equal(4, result.Estimate.SampleCount);
    }

    [Fact]
    public void DuplicateDiscardsPartialCycleAndDoesNotTreatDuplicateAsNewSample()
    {
        var analyzer = new SvEngineeringWindowAnalyzer();
        var key = CreateKey();
        var evidence = Evidence(200, 50);

        Assert.True(analyzer.TryObserve(
            Projection(key,
                EngineeringSample(0, 1),
                EngineeringSample(1, 0),
                EngineeringSample(1, 999),
                EngineeringSample(2, -1),
                EngineeringSample(3, 0),
                EngineeringSample(4, 1)),
            evidence,
            out var completed,
            out var reason), reason);

        Assert.Empty(completed);

        Assert.True(analyzer.TryObserve(
            Projection(key, EngineeringSample(5, 0)),
            evidence,
            out completed,
            out reason), reason);

        var result = Assert.Single(completed);
        Assert.Equal((ushort)2, result.FirstSampleCount);
        Assert.Equal((ushort)5, result.LastSampleCount);
    }

    [Fact]
    public void ExplicitCounterWrapMayCompleteAcrossNormalWrap()
    {
        var analyzer = new SvEngineeringWindowAnalyzer();
        var evidence = Evidence(200, 50) with
        {
            SampleCounterWrap = 4,
            SampleCounterWrapSource = SvFactSource.TrustedContext
        };

        var accepted = analyzer.TryObserve(
            Projection(CreateKey(),
                EngineeringSample(2, -1),
                EngineeringSample(3, 0),
                EngineeringSample(0, 1),
                EngineeringSample(1, 0)),
            evidence,
            out var completed,
            out var reason);

        Assert.True(accepted, reason);
        var result = Assert.Single(completed);
        Assert.Equal((ushort)2, result.FirstSampleCount);
        Assert.Equal((ushort)1, result.LastSampleCount);
    }

    [Fact]
    public void MetadataChangeResetsPartialCycleBeforeNewSignalIdentity()
    {
        var analyzer = new SvEngineeringWindowAnalyzer();
        var evidence = Evidence(200, 50) with
        {
            SampleCounterWrap = 4,
            SampleCounterWrapSource = SvFactSource.TrustedContext
        };
        var key = CreateKey();

        Assert.True(analyzer.TryObserve(
            Projection(key,
                EngineeringSample(0, 1, signalReference: "IA"),
                EngineeringSample(1, 0, signalReference: "IA")),
            evidence,
            out var beforeChange,
            out var beforeReason), beforeReason);
        Assert.Empty(beforeChange);

        Assert.True(analyzer.TryObserve(
            Projection(key,
                EngineeringSample(2, -1, signalReference: "IB"),
                EngineeringSample(3, 0, signalReference: "IB"),
                EngineeringSample(0, 1, signalReference: "IB"),
                EngineeringSample(1, 0, signalReference: "IB")),
            evidence,
            out var completed,
            out var reason), reason);

        var result = Assert.Single(completed);
        Assert.Equal("IB", result.SignalReference);
        Assert.Equal((ushort)2, result.FirstSampleCount);
        Assert.Equal((ushort)1, result.LastSampleCount);
    }

    [Fact]
    public void NonIntegralCycleOrInferredFrequencyFailsClosed()
    {
        var analyzer = new SvEngineeringWindowAnalyzer();
        var projection = Projection(CreateKey(), EngineeringSample(0, 1));

        Assert.False(analyzer.TryObserve(
            projection,
            Evidence(4_000, 60),
            out var nonIntegral,
            out var nonIntegralReason));
        Assert.Empty(nonIntegral);
        Assert.Contains("integral", nonIntegralReason, StringComparison.OrdinalIgnoreCase);

        Assert.False(analyzer.TryObserve(
            projection,
            Evidence(4_000, 50) with { FundamentalFrequencySource = SvFactSource.ProfileInferred },
            out var inferred,
            out var inferredReason));
        Assert.Empty(inferred);
        Assert.Contains("never guessed", inferredReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, analyzer.ActiveStreamCount);
    }

    [Fact]
    public void StreamAndChannelRegistriesStayBounded()
    {
        var streamBounded = new SvEngineeringWindowAnalyzer(new SvEngineeringWindowAnalysisOptions
        {
            MaximumStreams = 2,
            MaximumChannelsPerStream = 2,
            MaximumSamplesPerCycle = 80
        });
        var evidence = Evidence(4_000, 50);

        foreach (var pair in new[] { ("A", (ushort)0x4001), ("B", (ushort)0x4002), ("C", (ushort)0x4003) })
        {
            Assert.True(streamBounded.TryObserve(
                Projection(CreateKey(pair.Item1, pair.Item2), EngineeringSample(0, 1)),
                evidence,
                out _,
                out var reason), reason);
        }
        Assert.Equal(2, streamBounded.ActiveStreamCount);
        Assert.Equal(2, streamBounded.ActiveChannelCount);

        var channelBounded = new SvEngineeringWindowAnalyzer(new SvEngineeringWindowAnalysisOptions
        {
            MaximumStreams = 1,
            MaximumChannelsPerStream = 1,
            MaximumSamplesPerCycle = 80
        });
        var accepted = channelBounded.TryObserve(
            Projection(CreateKey(),
                EngineeringSample(0, 1, elementIndex: 0, signalReference: "IA"),
                EngineeringSample(0, 2, elementIndex: 2, signalReference: "IB")),
            evidence,
            out var completed,
            out var boundedReason);

        Assert.False(accepted);
        Assert.Empty(completed);
        Assert.Contains("bound", boundedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, channelBounded.ActiveChannelCount);
    }

    private static SvEngineeringWindowEvidence Evidence(double sampleRateHz, double fundamentalFrequencyHz)
        => new()
        {
            SampleRateHz = sampleRateHz,
            SampleRateSource = SvFactSource.CaptureCalculated,
            FundamentalFrequencyHz = fundamentalFrequencyHz,
            FundamentalFrequencySource = SvFactSource.TrustedContext
        };

    private static SvObservedStreamKey CreateKey(string svId = "MU01", ushort appId = 0x4000)
        => new()
        {
            SourceMac = "020000000001",
            DestinationMac = "010CCD040001",
            AppId = appId,
            VlanId = 10,
            SvId = svId,
            DataSetReference = "MU01LD0/LLN0$dsSV"
        };

    private static SvSclBoundMeasurementProjection Projection(
        SvObservedStreamKey key,
        params SvProjectedMeasurementSample[] samples)
        => new()
        {
            StreamKey = key,
            ControlBlockReference = "MU01LD0/LLN0.MSVCB01",
            IsBoundToScl = true,
            HasEngineeringValues = samples.Any(sample => sample.EngineeringValue.HasValue),
            Samples = samples
        };

    private static SvProjectedMeasurementSample EngineeringSample(
        ushort sampleCount,
        double value,
        int elementIndex = 0,
        string signalReference = "MU01LD0/TCTR1.AmpSv.instMag.i")
        => new()
        {
            SampleCount = sampleCount,
            ElementIndex = elementIndex,
            SignalReference = signalReference,
            Cdc = "SAV-current",
            RawValue = value,
            EngineeringValue = value,
            Scale = new SvEngineeringScale
            {
                Multiplier = 1.0,
                Unit = "A",
                Source = SvEngineeringScaleSource.ManualOverride,
                Confidence = SvEngineeringScaleConfidence.DeviceValidated,
                Reason = "Explicit test engineering scale."
            }
        };
}
