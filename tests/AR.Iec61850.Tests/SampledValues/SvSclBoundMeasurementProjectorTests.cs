using System.Buffers.Binary;
using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using AR.Iec61850.SampledValues.Measurements;
using AR.Iec61850.SampledValues.Profiles;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvSclBoundMeasurementProjectorTests
{
    [Fact]
    public void FixedSclBoundValueQualityLayoutProjectsEngineeringValuesWithoutGuessingDatasetOrder()
    {
        var profile = BuildFixedProtectionProfile();
        var payload = BuildFixedProtectionPayload(1_000);
        var frame = profile.CreateFrame(
            MacAddress.Parse("02:00:00:00:00:01"),
            sampleCount: 7,
            payload);

        var projection = SvSclBoundMeasurementProjector.Project(frame, profile);

        Assert.True(projection.IsBoundToScl);
        Assert.True(projection.HasEngineeringValues);
        Assert.Equal(8, projection.Samples.Count);
        Assert.All(projection.Samples, sample => Assert.Equal(7, sample.SampleCount));

        var current = projection.Samples.First(sample => sample.SignalReference.Contains("TCTR", StringComparison.Ordinal));
        Assert.Equal(1_000.0, current.RawValue, 9);
        Assert.Equal(1.0, Assert.IsType<double>(current.EngineeringValue), 9);
        Assert.Equal("A", current.Scale.Unit);
        Assert.Equal(SvEngineeringScaleSource.SclBackedLegacy92LeStyle, current.Scale.Source);
        Assert.Null(current.DomainValue);
        Assert.Null(current.DisplayValue);

        var voltage = projection.Samples.First(sample => sample.SignalReference.Contains("TVTR", StringComparison.Ordinal));
        Assert.Equal(10.0, Assert.IsType<double>(voltage.EngineeringValue), 9);
        Assert.Equal("V", voltage.Scale.Unit);
    }

    [Fact]
    public void ExplicitMeasurementContextEnablesPrimaryToSecondaryDisplayProjection()
    {
        var profile = BuildFixedProtectionProfile();
        var frame = profile.CreateFrame(
            MacAddress.Parse("02:00:00:00:00:01"),
            sampleCount: 8,
            BuildFixedProtectionPayload(1_000));
        var key = SvObservedStreamKey.FromFrame(frame);
        var context = new SvStreamMeasurementContext
        {
            StreamKey = key.Id,
            SvId = profile.Stream.SvId,
            WireDomain = SvMeasurementValueDomain.PrimaryEngineering,
            DisplayDomain = SvMeasurementValueDomain.SecondaryEquivalent,
            CurrentRatio = new SvMeasurementRatio
            {
                PrimaryNominal = 1_000,
                SecondaryNominal = 1,
                Unit = "A",
                Source = SvRatioSource.Manual,
                Reference = "test-current-ratio"
            },
            VoltageRatio = new SvMeasurementRatio
            {
                PrimaryNominal = 100_000,
                SecondaryNominal = 100,
                Unit = "V",
                Source = SvRatioSource.Manual,
                Reference = "test-voltage-ratio"
            }
        };

        var projection = SvSclBoundMeasurementProjector.Project(frame, profile, context);

        var current = projection.Samples.First(sample => sample.SignalReference.Contains("TCTR", StringComparison.Ordinal));
        Assert.NotNull(current.DomainValue);
        Assert.Equal(1.0, Assert.IsType<double>(current.DomainValue!.PrimaryValue), 9);
        Assert.Equal(0.001, Assert.IsType<double>(current.DomainValue.SecondaryEquivalentValue), 9);
        Assert.Equal(0.001, Assert.IsType<double>(current.DisplayValue), 9);
        Assert.Equal(SvRatioSource.Manual, current.DomainValue.RatioSource);
    }

    [Fact]
    public void NonFixedSclLayoutKeepsNumericValuesRawOnly()
    {
        var profile = BuildSingleChannelProfile();
        var payload = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 12_345);
        var frame = profile.CreateFrame(
            MacAddress.Parse("02:00:00:00:00:01"),
            sampleCount: 0,
            payload);

        var projection = SvSclBoundMeasurementProjector.Project(frame, profile);

        Assert.True(projection.IsBoundToScl);
        Assert.False(projection.HasEngineeringValues);
        var sample = Assert.Single(projection.Samples);
        Assert.Equal(12_345.0, sample.RawValue, 9);
        Assert.Null(sample.EngineeringValue);
        Assert.Equal(SvEngineeringScaleSource.RawOnly, sample.Scale.Source);
        Assert.Contains(projection.Diagnostics, item => item.Contains("raw only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EthernetIdentityMismatchFailsClosedBeforePayloadProjection()
    {
        var profile = BuildFixedProtectionProfile();
        var valid = profile.CreateFrame(
            MacAddress.Parse("02:00:00:00:00:01"),
            sampleCount: 0,
            BuildFixedProtectionPayload(1_000));
        var mismatch = new SampledValuesFrame
        {
            Destination = valid.Destination,
            Source = valid.Source,
            Vlan = valid.Vlan,
            AppId = 0x4999,
            Pdu = valid.Pdu
        };

        var projection = SvSclBoundMeasurementProjector.Project(mismatch, profile);

        Assert.False(projection.IsBoundToScl);
        Assert.Empty(projection.Samples);
        Assert.Contains(projection.Diagnostics, item => item.Contains("APPID", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigurationRevisionMismatchFailsClosedBeforePayloadProjection()
    {
        var profile = BuildFixedProtectionProfile();
        var valid = profile.CreateFrame(
            MacAddress.Parse("02:00:00:00:00:01"),
            sampleCount: 0,
            BuildFixedProtectionPayload(1_000));
        var original = valid.Pdu.Asdus[0];
        var mismatch = new SampledValuesFrame
        {
            Destination = valid.Destination,
            Source = valid.Source,
            Vlan = valid.Vlan,
            AppId = valid.AppId,
            Pdu = new SampledValuesPdu
            {
                Asdus =
                [
                    new SampledValueAsdu
                    {
                        SvId = original.SvId,
                        DataSetReference = original.DataSetReference,
                        SampleCount = original.SampleCount,
                        ConfigurationRevision = original.ConfigurationRevision + 1,
                        SampleSynchronization = original.SampleSynchronization,
                        SampleRate = original.SampleRate,
                        SampleMode = original.SampleMode,
                        SamplePayload = original.SamplePayload
                    }
                ]
            }
        };

        var projection = SvSclBoundMeasurementProjector.Project(mismatch, profile);

        Assert.False(projection.IsBoundToScl);
        Assert.Empty(projection.Samples);
        Assert.Contains(projection.Diagnostics, item => item.Contains("confRev", StringComparison.Ordinal));
    }

    [Fact]
    public void MismatchedMeasurementContextDoesNotApplyCtVtRatio()
    {
        var profile = BuildFixedProtectionProfile();
        var frame = profile.CreateFrame(
            MacAddress.Parse("02:00:00:00:00:01"),
            sampleCount: 0,
            BuildFixedProtectionPayload(1_000));
        var context = new SvStreamMeasurementContext
        {
            StreamKey = "SV|different-stream",
            SvId = profile.Stream.SvId,
            WireDomain = SvMeasurementValueDomain.PrimaryEngineering,
            DisplayDomain = SvMeasurementValueDomain.SecondaryEquivalent,
            CurrentRatio = new SvMeasurementRatio
            {
                PrimaryNominal = 1_000,
                SecondaryNominal = 1,
                Unit = "A",
                Source = SvRatioSource.Manual
            }
        };

        var projection = SvSclBoundMeasurementProjector.Project(frame, profile, context);

        Assert.True(projection.HasEngineeringValues);
        Assert.All(projection.Samples, sample => Assert.Null(sample.DomainValue));
        Assert.Contains(projection.Diagnostics, item => item.Contains("stream key", StringComparison.OrdinalIgnoreCase));
    }

    private static SampledValuesPublisherProfile BuildFixedProtectionProfile()
    {
        var entries = new List<SclDataSetEntry>();
        var index = 0;
        for (var channel = 1; channel <= 4; channel++)
        {
            entries.Add(ValueEntry(index++, $"MU01/TCTR{channel}.AmpSv.instMag.i"));
            entries.Add(QualityEntry(index++, $"MU01/TCTR{channel}.AmpSv.q"));
        }
        for (var channel = 1; channel <= 4; channel++)
        {
            entries.Add(ValueEntry(index++, $"MU01/TVTR{channel}.VolSv.instMag.i"));
            entries.Add(QualityEntry(index++, $"MU01/TVTR{channel}.VolSv.q"));
        }

        return SampledValuesPublisherProfile.Create(new SclSampledValuesStream
        {
            IedName = "MU01",
            LdInst = "MU",
            ControlName = "MSVCB01",
            ControlBlockReference = "MU01/LLN0$MSVCB01",
            DataSetName = "PhsMeas1",
            DataSetReference = "MU01/LLN0$PhsMeas1",
            ConfigurationRevision = 1,
            SvId = "MU01_SV01",
            SampleRate = 4_000,
            SampleMode = "SmpPerSec",
            NoAsdu = 1,
            Address = new SclStreamAddress
            {
                AppId = 0x4000,
                DestinationMac = MacAddress.Parse("01:0C:CD:04:00:01")
            },
            Entries = entries
        });
    }

    private static SampledValuesPublisherProfile BuildSingleChannelProfile()
        => SampledValuesPublisherProfile.Create(new SclSampledValuesStream
        {
            IedName = "MU01",
            LdInst = "MU",
            ControlName = "MSVCB01",
            ControlBlockReference = "MU01/LLN0$MSVCB01",
            DataSetName = "OneCurrent",
            DataSetReference = "MU01/LLN0$OneCurrent",
            ConfigurationRevision = 1,
            SvId = "MU01_SV01",
            SampleRate = 4_000,
            SampleMode = "SmpPerSec",
            NoAsdu = 1,
            Address = new SclStreamAddress
            {
                AppId = 0x4000,
                DestinationMac = MacAddress.Parse("01:0C:CD:04:00:01")
            },
            Entries =
            [
                ValueEntry(0, "MU01/TCTR1.AmpSv.instMag.i"),
                QualityEntry(1, "MU01/TCTR1.AmpSv.q")
            ]
        });

    private static SclDataSetEntry ValueEntry(int index, string reference)
        => new()
        {
            Index = index,
            SignalReference = reference,
            Cdc = "SAV",
            BType = "INT32"
        };

    private static SclDataSetEntry QualityEntry(int index, string reference)
        => new()
        {
            Index = index,
            SignalReference = reference,
            Cdc = "SAV",
            BType = "Quality",
            IsQuality = true
        };

    private static byte[] BuildFixedProtectionPayload(int rawValue)
    {
        var payload = new byte[64];
        for (var channel = 0; channel < 8; channel++)
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(channel * 8, 4), rawValue);
        return payload;
    }
}
