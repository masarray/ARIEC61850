using System.Buffers.Binary;
using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using AR.Iec61850.SampledValues.Analysis;
using AR.Iec61850.SampledValues.Profiles;
using AR.Iec61850.Scl;
using AR.Iec61850.Transports;

namespace AR.Iec61850.Tests.SampledValues;

public sealed class SvSustainedCaptureProcessorTests
{
    [Fact]
    public async Task RawEthernetSourceUsesCanonicalParserAndDoesNotAdvanceOnParserReject()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var firstBytes = SampledValuesFrameBuilder.BuildEthernetFrame(BuildOpaqueFrame(0));
        var secondBytes = SampledValuesFrameBuilder.BuildEthernetFrame(BuildOpaqueFrame(1));
        var source = new InMemoryProcessBusFrameSource(
        [
            new ProcessBusCapturedFrame { Timestamp = start, Frame = firstBytes },
            new ProcessBusCapturedFrame { Timestamp = start.AddTicks(1), Frame = new byte[] { 1, 2, 3, 4 } },
            new ProcessBusCapturedFrame { Timestamp = start.AddTicks(2), Frame = secondBytes }
        ]);
        var processor = new SvSustainedCaptureProcessor();
        var results = new List<SvCapturedFrameProcessingResult>();

        await foreach (var result in processor.ProcessAsync(
                           source,
                           new ProcessBusCaptureOptions(),
                           SvObservationInputKind.PcapReplay))
        {
            results.Add(result);
        }

        Assert.Equal(3, results.Count);
        Assert.Equal(SvCapturedFrameProcessingStatus.Accepted, results[0].Status);
        Assert.Equal(SvCapturedFrameProcessingStatus.ParserRejected, results[1].Status);
        Assert.Equal(SvCapturedFrameProcessingStatus.Accepted, results[2].Status);
        Assert.Null(results[1].Observation);
        Assert.Equal(4, results[1].CapturedLength);
        Assert.Equal(1, processor.Session.ObservationStreamCount);
        Assert.Equal(1, processor.Session.SustainedStreamCount);
        Assert.Equal(2, results[2].Observation!.Sustained.FrameCount);
        Assert.Contains(SvObservationInputKind.PcapReplay, results[2].Observation!.Observation.InputKinds);
    }

    [Fact]
    public async Task RawEthernetFramesCanCompleteEvidenceBackedEngineeringCycle()
    {
        const int samplesPerCycle = 80;
        const double rawPeak = 100_000.0;
        var profile = BuildFixedProtectionProfile();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var frames = new List<ProcessBusCapturedFrame>(samplesPerCycle);
        for (var index = 0; index < samplesPerCycle; index++)
        {
            var raw = checked((int)Math.Round(
                rawPeak * Math.Cos(2.0 * Math.PI * index / samplesPerCycle),
                MidpointRounding.AwayFromZero));
            var frame = profile.CreateFrame(
                MacAddress.Parse("02:00:00:00:00:01"),
                sampleCount: (ushort)index,
                BuildFixedProtectionPayload(raw));
            frames.Add(new ProcessBusCapturedFrame
            {
                Timestamp = start.AddTicks(index * 2_500L),
                Frame = SampledValuesFrameBuilder.BuildEthernetFrame(frame)
            });
        }

        var source = new InMemoryProcessBusFrameSource(frames);
        var processor = new SvSustainedCaptureProcessor();
        SvCapturedFrameProcessingResult? final = null;
        await foreach (var result in processor.ProcessAsync(
                           source,
                           new ProcessBusCaptureOptions(),
                           SvObservationInputKind.PcapReplay,
                           _ => new SvSustainedFrameContext
                           {
                               Profile = profile,
                               NominalFrequencyHz = 50,
                               EngineeringWindowEvidence = new SvEngineeringWindowEvidence
                               {
                                   SampleRateHz = 4_000,
                                   SampleRateSource = SvFactSource.SclDerived,
                                   FundamentalFrequencyHz = 50,
                                   FundamentalFrequencySource = SvFactSource.TrustedContext
                               }
                           }))
        {
            final = result;
        }

        Assert.NotNull(final);
        Assert.Equal(SvCapturedFrameProcessingStatus.Accepted, final!.Status);
        Assert.NotNull(final.Observation);
        Assert.Null(final.Observation!.EngineeringWindowDiagnostic);
        Assert.Equal(8, final.Observation.EngineeringWindows.Count);
        var current = final.Observation.EngineeringWindows.Single(window =>
            window.SignalReference.Contains("TCTR1", StringComparison.Ordinal));
        Assert.InRange(current.Estimate.Rms, 70.70, 70.72);
        Assert.InRange(Math.Abs(current.Estimate.AngleDegrees), 0, 0.01);
        Assert.Equal(1, processor.Session.EngineeringWindowStreamCount);
        Assert.Equal(8, processor.Session.EngineeringWindowChannelCount);
    }

    private static SampledValuesFrame BuildOpaqueFrame(ushort sampleCount)
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
                        DataSetReference = "MU01/LLN0$Dataset1",
                        SampleCount = sampleCount,
                        SampleSynchronization = 2,
                        SampleRate = 4_000,
                        SampleMode = 1,
                        SamplePayload = new byte[8]
                    }
                ]
            }
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
