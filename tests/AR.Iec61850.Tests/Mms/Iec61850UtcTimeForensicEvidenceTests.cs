using System.Buffers.Binary;
using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class Iec61850UtcTimeForensicEvidenceTests
{
    [Fact]
    public void MmsDecode_Preserves_Exact_Eight_Byte_UtcTime_And_RoundTrips_It()
    {
        var raw = CreateRawUtcTime(31, 0x33, 0x5A, 0x86, 0xA5);
        var tag = BerWriter.EncodeIdentifier(BerClass.ContextSpecific, false, 17);
        var encoded = BerWriter.EncodeTlv(tag, raw);

        var value = Assert.Single(MmsDataCodec.DecodeAllData(encoded));
        var evidence = Iec61850UtcTimeEvidence.Decode(value);

        Assert.Equal(MmsDataKind.UtcTime, value.Kind);
        Assert.Equal(raw, value.RawValue.ToArray());
        Assert.Equal(encoded, MmsDataCodec.Encode(value));
        Assert.True(evidence.IsDecoded);
        Assert.True(evidence.HasWireProvenance);
        Assert.Equal(Convert.ToHexString(raw), evidence.RawHex);
        Assert.Equal(0x335A86, evidence.FractionOfSecond24);
        Assert.Equal("335A86", evidence.FractionOfSecondHex);
        Assert.Equal((byte)0xA5, evidence.Quality);
        Assert.True(evidence.LeapSecondsKnown);
        Assert.False(evidence.ClockFailure);
        Assert.True(evidence.ClockNotSynchronized);
        Assert.False(evidence.ClockSynchronized);
        Assert.Equal(5, evidence.AccuracyCode);
        Assert.Equal("2^-5 s", evidence.TimeAccuracy);
        Assert.Matches(@"[+-]\d{2}:\d{2}$", evidence.FullPrecisionLocal);
    }

    [Fact]
    public void Synthetic_UtcTime_Does_Not_Pretend_To_Have_Wire_Provenance()
    {
        var raw = CreateRawUtcTime(31, 0x33, 0x5A, 0x86, 0x00);
        var synthetic = MmsDataValue.UtcTime(Iec61850UtcTime.FromBytes(raw));

        var evidence = Iec61850UtcTimeEvidence.Decode(synthetic);

        Assert.True(evidence.IsDecoded);
        Assert.False(evidence.HasWireProvenance);
        Assert.Equal(string.Empty, evidence.RawHex);
        Assert.Null(evidence.SecondsSinceEpoch);
        Assert.Null(evidence.FractionOfSecond24);
    }

    [Fact]
    public void Report_Evidence_Separates_Ied_Timestamp_TimeOfEntry_And_Client_Receive_Time()
    {
        var iedRaw = CreateRawUtcTime(31, 0x33, 0x5A, 0x86, 0x00);
        var reportRaw = CreateRawUtcTime(32, 0x10, 0x00, 0x00, 0x80);
        var iedValue = DecodeRawUtcTime(iedRaw);
        var reportValue = DecodeRawUtcTime(reportRaw);
        var receivedAt = new DateTimeOffset(2026, 8, 13, 10, 0, 33, 456, TimeSpan.Zero).AddTicks(7890);

        var decoded = new MmsInformationReport
        {
            IsSuccess = true,
            Items =
            [
                new MmsInformationReportItem { Index = 0, Value = MmsDataValue.VisibleString("LD0/LLN0$BR$brcbA01") },
                new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(5, [0x60]) },
                new MmsInformationReportItem { Index = 2, Value = MmsDataValue.Unsigned(7) },
                new MmsInformationReportItem { Index = 3, Value = reportValue },
                new MmsInformationReportItem { Index = 4, Value = MmsDataValue.BitString(7, [0x80]) }
            ],
            Message = "decoded"
        };

        var frame = new MmsReportFrame
        {
            ReceivedAt = receivedAt,
            Header = new MmsReportHeader
            {
                ReportId = "LD0/LLN0$BR$brcbA01",
                TimeOfEntry = MmsDataValueRenderer.ToCompactString(reportValue)
            },
            InclusionBitstringItemIndex = 4,
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = "LD0/XCBR1.Pos.t",
                        FunctionalConstraint = "ST"
                    },
                    Value = iedValue,
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };

        var evidence = MmsReportTimestampEvidence.FromFrame(frame, decoded);
        var ied = Assert.Single(evidence.IedTimestamps);

        Assert.Equal("IED .t", ied.Source);
        Assert.Equal("LD0/XCBR1.Pos.t", ied.Reference);
        Assert.Equal(Convert.ToHexString(iedRaw), ied.Timestamp.RawHex);
        Assert.Equal(Convert.ToHexString(reportRaw), evidence.ReportTimeOfEntry.RawHex);
        Assert.True(evidence.HasReportTimeOfEntryWireEvidence);
        Assert.NotEqual(ied.Timestamp.FullPrecisionUtc, evidence.ReportTimeOfEntry.FullPrecisionUtc);
        Assert.Contains("2026-08-13 10:00:33.4567890 UTC", evidence.ReceivedAtUtc, StringComparison.Ordinal);
        Assert.Matches(@"[+-]\d{2}:\d{2}$", evidence.ReceivedAtLocal);
    }

    private static MmsDataValue DecodeRawUtcTime(byte[] raw)
    {
        var tag = BerWriter.EncodeIdentifier(BerClass.ContextSpecific, false, 17);
        var encoded = BerWriter.EncodeTlv(tag, raw);
        return Assert.Single(MmsDataCodec.DecodeAllData(encoded));
    }

    private static byte[] CreateRawUtcTime(int second, byte fractionHigh, byte fractionMid, byte fractionLow, byte quality)
    {
        var unixSeconds = new DateTimeOffset(2026, 8, 13, 10, 0, second, TimeSpan.Zero).ToUnixTimeSeconds();
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), checked((uint)unixSeconds));
        bytes[4] = fractionHigh;
        bytes[5] = fractionMid;
        bytes[6] = fractionLow;
        bytes[7] = quality;
        return bytes;
    }
}
