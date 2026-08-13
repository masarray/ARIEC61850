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

    private static Iec61850UtcTime DecodeCustomerCase()
    {
        var seconds = new DateTimeOffset(2026, 8, 13, 10, 0, 31, TimeSpan.Zero).ToUnixTimeSeconds();
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes[..4], checked((uint)seconds));

        // 0x335A86 / 2^24 = 0.200600028... seconds, which maps exactly
        // to 2,006,000 .NET ticks (31.2006000) after nearest-tick conversion.
        bytes[4] = 0x33;
        bytes[5] = 0x5A;
        bytes[6] = 0x86;
        bytes[7] = 0x00;

        return Iec61850UtcTime.FromBytes(bytes);
    }
}
