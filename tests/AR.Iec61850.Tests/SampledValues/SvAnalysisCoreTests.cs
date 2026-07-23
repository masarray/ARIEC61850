using AR.Iec61850.SampledValues;
using AR.Iec61850.SampledValues.Analysis;
using AR.Iec61850.SampledValues.Measurements;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvAnalysisCoreTests
{
    [Fact]
    public void GenericPayloadInspectorPreservesRawRepresentationsWithoutSemantics()
    {
        var inspection = SvGenericPayloadInspector.Inspect(new byte[]
        {
            0x00, 0x00, 0x03, 0xE8,
            0xFF, 0xFF, 0xFC, 0x18
        });

        Assert.Equal(8, inspection.PayloadLength);
        Assert.True(inspection.HasEightByteGroupShape);
        Assert.Equal(2, inspection.Words.Count);
        Assert.Equal(1000, inspection.Words[0].SignedInt32);
        Assert.Equal(-1000, inspection.Words[1].SignedInt32);
        Assert.Equal("Word 1", inspection.Words[0].GenericLabel);
        Assert.Contains(inspection.Diagnostics, item => item.Contains("structural evidence only", StringComparison.Ordinal));
    }

    [Fact]
    public void GenericPayloadInspectorPreservesTrailingBytes()
    {
        var inspection = SvGenericPayloadInspector.Inspect(new byte[]
        {
            0x00, 0x00, 0x00, 0x01,
            0xAA, 0xBB
        });

        Assert.False(inspection.IsFourByteAligned);
        Assert.Equal(1, inspection.CompleteWordCount);
        Assert.Equal(2, inspection.TrailingByteCount);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, inspection.TrailingBytes);
    }

    [Fact]
    public void GenericAsduInspectorExposesWireFieldsButKeepsMappingUnresolved()
    {
        var asdu = new SampledValueAsdu
        {
            SvId = "MU01",
            DataSetReference = "MU01/LLN0$Dataset1",
            SampleCount = 37,
            ConfigurationRevision = 4,
            SampleSynchronization = 2,
            SampleRate = 4000,
            SampleMode = 1,
            SamplePayload = new byte[8]
        };

        var inspection = SvGenericAsduInspector.Inspect(asdu);

        Assert.Equal("MU01", inspection.SvId);
        Assert.Equal((ushort)37, inspection.SampleCount);
        Assert.Contains("bind SCL", inspection.MappingState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("smpRate", inspection.OptionalFieldSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ScalingNeverInfersEngineeringUnitsWithoutSclBinding()
    {
        var scale = SvEngineeringScaleResolver.Resolve(new SvEngineeringScaleEvidence
        {
            Channel = "Ia",
            Kind = "Current",
            IsSclBound = false,
            IsFixedFourCurrentFourVoltageLayout = true,
            AnalogChannelCount = 8,
            PayloadBytesPerAsdu = 64,
            DeclaredSampleMode = 1,
            DeclaredSampleRate = 4000
        });

        Assert.Equal(SvEngineeringScaleSource.RawOnly, scale.Source);
        Assert.Equal("count", scale.Unit);
        Assert.Equal(1000, scale.Apply(1000));
    }

    [Fact]
    public void ScalingRequiresSclLayoutAndProtectionRateEvidence()
    {
        var scale = SvEngineeringScaleResolver.Resolve(new SvEngineeringScaleEvidence
        {
            Channel = "TCTR1/AmpSv.instMag.i",
            Kind = "Current",
            IsSclBound = true,
            IsFixedFourCurrentFourVoltageLayout = true,
            AnalogChannelCount = 8,
            PayloadBytesPerAsdu = 64,
            DeclaredSampleMode = 1,
            DeclaredSampleRate = 4000
        });

        Assert.Equal(SvEngineeringScaleSource.SclBackedLegacy92LeStyle, scale.Source);
        Assert.Equal("A", scale.Unit);
        Assert.Equal(1.0, scale.Apply(1000), 9);
    }

    [Theory]
    [InlineData(4000, 50)]
    [InlineData(4800, 60)]
    public void TimebaseResolverUsesEvidenceForLegacyProtectionRate(double rate, double frequency)
    {
        var result = SvTimebaseResolver.Resolve(new SvTimebaseEvidence
        {
            ObservedSamplesPerSecond = rate,
            IsFixedLegacyProtectionLayout = true
        });

        Assert.Equal(frequency, result.NominalFrequencyHz);
        Assert.Equal(80, result.SamplesPerCycle);
        Assert.Equal((ushort)rate, result.SampleCounterWrap);
    }

    [Fact]
    public void TimebaseResolverDoesNotInventFrequencyForUnknownLayout()
    {
        var result = SvTimebaseResolver.Resolve(new SvTimebaseEvidence
        {
            DeclaredSampleMode = 1,
            DeclaredSampleRate = 4000,
            IsFixedLegacyProtectionLayout = false
        });

        Assert.Null(result.NominalFrequencyHz);
        Assert.Null(result.SamplesPerCycle);
        Assert.Equal((ushort)4000, result.SampleCounterWrap);
    }

    [Fact]
    public void CounterTrackerDistinguishesNormalWrapAndGap()
    {
        var tracker = new SvSampleCounterTracker();
        tracker.Observe(3998, 4000);
        tracker.Observe(3999, 4000);
        Assert.Equal(SvSampleCounterTransitionKind.NormalWrap, tracker.Observe(0, 4000).Kind);

        var gap = tracker.Observe(3, 4000);
        Assert.Equal(SvSampleCounterTransitionKind.Gap, gap.Kind);
        Assert.Equal(2, gap.MissingSamples);
    }

    [Fact]
    public void QualityDecoderReportsSemanticFlagsAndSeverity()
    {
        var state = SvQualityDecoder.DecodeWord(1 << 6);

        Assert.True(state.Failure);
        Assert.Equal(SvQualityValidity.Good, state.Validity);
        Assert.Equal(SvQualitySeverity.Bad, state.Severity);
        Assert.Contains("failure", state.ActiveFlags);
    }

    [Fact]
    public void MeasurementRatioConvertsOnlyFromExplicitContext()
    {
        var ratio = new SvMeasurementRatio
        {
            PrimaryNominal = 1000,
            SecondaryNominal = 1,
            Unit = "A",
            Source = SvRatioSource.DeviceConfiguration,
            Reference = "reviewed setting"
        };

        var value = SvMeasurementDomainResolver.Resolve(
            500,
            "A",
            SvMeasurementValueDomain.PrimaryEngineering,
            ratio);

        Assert.Equal(500, value.PrimaryValue);
        Assert.Equal(0.5, value.SecondaryEquivalentValue, 9);
        Assert.Equal(SvRatioSource.DeviceConfiguration, value.RatioSource);
    }

    [Fact]
    public void MeasurementContextJsonRoundTripsAndRejectsDuplicateKeys()
    {
        var context = new SvStreamMeasurementContext
        {
            StreamKey = "stream-1",
            SvId = "MU01",
            CurrentRatio = new SvMeasurementRatio
            {
                PrimaryNominal = 1000,
                SecondaryNominal = 1,
                Unit = "A",
                Source = SvRatioSource.Manual
            }
        };
        var document = new SvMeasurementContextDocument { Streams = [context] };

        var restored = SvMeasurementContextSerializer.FromJson(
            SvMeasurementContextSerializer.ToJson(document));
        Assert.Equal("MU01", Assert.Single(restored.Streams).SvId);

        var duplicate = document with { Streams = [context, context] };
        Assert.Throws<InvalidDataException>(() => SvMeasurementContextSerializer.ToJson(duplicate));
    }
}
