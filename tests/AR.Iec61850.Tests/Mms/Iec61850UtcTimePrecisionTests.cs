using System.Buffers.Binary;
using AR.Iec61850.Binding;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class Iec61850UtcTimePrecisionTests
{
    [Fact]
    public void FromBytes_Preserves_Customer_SubMillisecond_Fraction_At_DotNet_Tick_Resolution()
    {
        var utc = DecodeCustomerCase();
        var expected = DateTimeOffset.FromUnixTimeSeconds(utc.Value.ToUnixTimeSeconds()).AddTicks(2_006_000);

        Assert.Equal(expected, utc.Value);
        Assert.Equal(2_006_000, utc.Value.Ticks % TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void TimestampDecoder_Does_Not_Truncate_Customer_2006000_Fraction_To_Milliseconds()
    {
        var utc = DecodeCustomerCase();

        var decoded = Iec61850TimestampDecoder.Decode(MmsDataValue.UtcTime(utc));

        Assert.True(decoded.IsDecoded);
        Assert.True(decoded.DisplayTime.EndsWith("31.2006000", StringComparison.Ordinal), decoded.DisplayTime);
    }

    [Fact]
    public void EngineeringFormatter_Exposes_Five_Fractional_Digits_Without_Changing_Ticks()
    {
        var utc = DecodeCustomerCase();
        var originalTicks = utc.Value.Ticks;

        var display = Iec61850UtcTimeFormatter.FormatEngineeringUtcTimestamp(utc);

        Assert.Equal("2026-08-13 10:00:31.20060 UTC", display);
        Assert.Equal(originalTicks, utc.Value.Ticks);
    }

    [Fact]
    public void ReportProjector_Preserves_Customer_2006000_Fraction_End_To_End()
    {
        var utc = DecodeCustomerCase();
        var frame = new MmsReportFrame
        {
            ReceivedAt = DateTimeOffset.UtcNow,
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
                    Value = MmsDataValue.UtcTime(utc),
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };

        var update = Assert.Single(MmsReportValueProjector.Project(frame).Updates);

        Assert.True(update.HasTimestamp);
        Assert.True(update.Timestamp.EndsWith("31.2006000", StringComparison.Ordinal), update.Timestamp);
    }

    [Fact]
    public void MmsDisplayString_Preserves_Full_DotNet_Timestamp_Precision()
    {
        var utc = DecodeCustomerCase();

        var display = MmsDataCodec.ToDisplayString(MmsDataValue.UtcTime(utc));

        Assert.Contains("31.2006000 UTC", display, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredRenderer_Preserves_Full_UtcTime_Precision()
    {
        var utc = DecodeCustomerCase();
        var value = MmsDataValue.Structure([
            MmsDataValue.Boolean(true),
            MmsDataValue.BitString(3, [0x00, 0x00]),
            MmsDataValue.UtcTime(utc)
        ]);

        var display = MmsDataValueRenderer.ToCompactString(value, "LD0/XCBR1.Pos");

        Assert.Contains("t=2026-08-13 10:00:31.2006000 UTC", display, StringComparison.Ordinal);
        Assert.DoesNotContain("31.200 UTC", display, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportHeader_TimeOfEntry_Preserves_Full_UtcTime_Precision()
    {
        var utc = DecodeCustomerCase();
        var decoded = new MmsInformationReport
        {
            IsSuccess = true,
            Items =
            [
                new MmsInformationReportItem { Index = 0, Value = MmsDataValue.VisibleString("LD0/LLN0$BR$brcbA01") },
                new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(6, [0x7B, 0x80]) },
                new MmsInformationReportItem { Index = 2, Value = MmsDataValue.Unsigned(7) },
                new MmsInformationReportItem { Index = 3, Value = MmsDataValue.UtcTime(utc) },
                new MmsInformationReportItem { Index = 4, Value = MmsDataValue.VisibleString("LD0/LLN0$DataSet") },
                new MmsInformationReportItem { Index = 5, Value = MmsDataValue.Boolean(false) },
                new MmsInformationReportItem { Index = 6, Value = MmsDataValue.OctetString(Convert.FromHexString("0000000000000014")) },
                new MmsInformationReportItem { Index = 7, Value = MmsDataValue.Unsigned(1) }
            ],
            Message = "decoded"
        };

        var header = MmsReportFrameMapper.DecodeHeader(decoded);

        Assert.Contains("31.2006000 UTC", header.TimeOfEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("31.200 UTC", header.TimeOfEntry, StringComparison.Ordinal);
    }

    private static Iec61850UtcTime DecodeCustomerCase()
    {
        var seconds = new DateTimeOffset(2026, 8, 13, 10, 0, 31, TimeSpan.Zero).ToUnixTimeSeconds();
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes[..4], checked((uint)seconds));
        bytes[4] = 0x33;
        bytes[5] = 0x5A;
        bytes[6] = 0x86;
        bytes[7] = 0x00;

        return Iec61850UtcTime.FromBytes(bytes);
    }
}
