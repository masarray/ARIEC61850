using System.Buffers.Binary;
using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues.Analysis;
using AR.Iec61850.SampledValues.Profiles;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvSustainedObservationSessionEngineeringTests
{
    [Fact]
    public void ExplicitSclAndTimebaseEvidenceProducesOneCycleEngineeringResultsThroughSession()
    {
        const int samplesPerCycle = 80;
        const double rawPeak = 100_000.0;
        const double phaseDegrees = 30.0;
        var phaseRadians = phaseDegrees * Math.PI / 180.0;
        var profile = BuildFixedProtectionProfile();
        var session = new SvSustainedObservationSession();
        var evidence = Evidence(4_000, 50);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        SvSustainedObservationResult result = new();

        for (var index = 0; index < samplesPerCycle; index++)
        {
            var raw = checked((int)Math.Round(
                rawPeak * Math.Cos((2.0 * Math.PI * index / samplesPerCycle) + phaseRadians),
                MidpointRounding.AwayFromZero));
            var frame = profile.CreateFrame(
                MacAddress.Parse("02:00:00:00:00:01"),
                sampleCount: (ushort)index,
                BuildFixedProtectionPayload(raw));

            Assert.True(session.TryObserve(
                start.AddTicks(index * 2_500L),
                frame,
                SvObservationInputKind.PcapReplay,
                out result,
                profile: profile,
                nominalFrequencyHz: 50,
                engineeringWindowEvidence: evidence));
        }

        Assert.NotNull(result.MeasurementProjection);
        Assert.True(result.MeasurementProjection!.IsBoundToScl);
        Assert.True(result.MeasurementProjection.HasEngineeringValues);
        Assert.Null(result.EngineeringWindowDiagnostic);
        Assert.Equal(8, result.EngineeringWindows.Count);
        Assert.Equal(1, session.EngineeringWindowStreamCount);
        Assert.Equal(8, session.EngineeringWindowChannelCount);

        var current = result.EngineeringWindows.Single(window =>
            window.SignalReference.Contains("TCTR1", StringComparison.Ordinal));
        Assert.Equal("A", current.EngineeringUnit);
        Assert.Equal(samplesPerCycle, current.SamplesPerCycle);
        Assert.Equal((ushort)0, current.FirstSampleCount);
        Assert.Equal((ushort)79, current.LastSampleCount);
        Assert.InRange(current.Estimate.Rms, 70.70, 70.72);
        Assert.InRange(current.Estimate.MagnitudeRms, 70.70, 70.72);
        Assert.InRange(current.Estimate.AngleDegrees, 29.99, 30.01);

        session.Clear();
        Assert.Equal(0, session.EngineeringWindowStreamCount);
        Assert.Equal(0, session.EngineeringWindowChannelCount);
    }

    [Fact]
    public void EvidenceWithoutSclProfileDoesNotCreateEngineeringState()
    {
        var session = new SvSustainedObservationSession();
        var frame = BuildOpaqueFrame();

        Assert.True(session.TryObserve(
            DateTimeOffset.UnixEpoch,
            frame,
            SvObservationInputKind.LiveCapture,
            out var result,
            engineeringWindowEvidence: Evidence(4_000, 50)));

        Assert.Null(result.MeasurementProjection);
        Assert.Empty(result.EngineeringWindows);
        Assert.Contains("without an explicit SCL publisher profile", result.EngineeringWindowDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, session.EngineeringWindowStreamCount);
        Assert.Equal(0, session.EngineeringWindowChannelCount);
    }

    [Fact]
    public void InferredFrequencyEvidenceDoesNotRejectCanonicalObservationButFailsClosedForEngineeringWindow()
    {
        var profile = BuildFixedProtectionProfile();
        var frame = profile.CreateFrame(
            MacAddress.Parse("02:00:00:00:00:01"),
            sampleCount: 0,
            BuildFixedProtectionPayload(1_000));
        var inferred = Evidence(4_000, 50) with
        {
            FundamentalFrequencySource = SvFactSource.ProfileInferred
        };
        var session = new SvSustainedObservationSession();

        Assert.True(session.TryObserve(
            DateTimeOffset.UnixEpoch,
            frame,
            SvObservationInputKind.LiveCapture,
            out var result,
            profile: profile,
            nominalFrequencyHz: 50,
            engineeringWindowEvidence: inferred));

        Assert.Equal(1, session.ObservationStreamCount);
        Assert.Equal(1, session.SustainedStreamCount);
        Assert.NotNull(result.MeasurementProjection);
        Assert.Empty(result.EngineeringWindows);
        Assert.Contains("never guessed", result.EngineeringWindowDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, session.EngineeringWindowStreamCount);
    }

    private static SvEngineeringWindowEvidence Evidence(double sampleRateHz, double fundamentalFrequencyHz)
        => new()
        {
            SampleRateHz = sampleRateHz,
            SampleRateSource = SvFactSource.SclDerived,
            FundamentalFrequencyHz = fundamentalFrequencyHz,
            FundamentalFrequencySource = SvFactSource.TrustedContext
        };

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

    private static SampledValuesFrame BuildOpaqueFrame()
        => new()
        {
            Destination = MacAddress.Parse("01:0C:CD:04:00:01"),
            Source = MacAddress.Parse("02:00:00:00:00:01"),
            AppId = 0x4000,
            Pdu = new SampledValuesPdu
            {
                Asdus =
                [
                    new SampledValueAsdu
                    {
                        SvId = "MU01_SV01",
                        DataSetReference = "MU01/LLN0$PhsMeas1",
                        SampleCount = 0,
                        SampleSynchronization = 2,
                        SampleRate = 4_000,
                        SampleMode = 1,
                        SamplePayload = new byte[8]
                    }
                ]
            }
        };

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
