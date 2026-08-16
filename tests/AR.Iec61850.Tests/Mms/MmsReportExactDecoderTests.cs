using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportExactDecoderTests
{
    [Fact]
    public void ReportFrameMapper_UsesOptFldsDrivenDecodeForRealisticBufferedReportShape()
    {
        var decoded = new MmsInformationReport
        {
            IsSuccess = true,
            Items =
            [
                new MmsInformationReportItem { Index = 0, Value = MmsDataValue.VisibleString("LD0/LLN0$BR$brcbA01") },
                new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(6, [0x7B, 0x80]) },
                new MmsInformationReportItem { Index = 2, Value = MmsDataValue.Unsigned(0) },
                new MmsInformationReportItem { Index = 3, Value = MmsDataValue.BinaryTime(Convert.FromHexString("04B1EE6A3C8F")) },
                new MmsInformationReportItem { Index = 4, Value = MmsDataValue.VisibleString("LD0/LLN0$DataSet") },
                new MmsInformationReportItem { Index = 5, Value = MmsDataValue.Boolean(false) },
                new MmsInformationReportItem { Index = 6, Value = MmsDataValue.OctetString(Convert.FromHexString("0000000000000014")) },
                new MmsInformationReportItem { Index = 7, Value = MmsDataValue.Unsigned(1) },
                new MmsInformationReportItem { Index = 8, Value = MmsDataValue.BitString(6, [0xC0]) },
                new MmsInformationReportItem { Index = 9, Value = MmsDataValue.Boolean(true) },
                new MmsInformationReportItem { Index = 10, Value = MmsDataValue.Boolean(false) },
                new MmsInformationReportItem { Index = 11, Value = MmsDataValue.BitString(2, [0x04]) },
                new MmsInformationReportItem { Index = 12, Value = MmsDataValue.BitString(2, [0x04]) }
            ],
            Message = "decoded"
        };
        var members = new[]
        {
            new MmsDataSetDirectoryMember { UserReference = "LD0/GGIO1.Ind1.stVal", FunctionalConstraint = "ST" },
            new MmsDataSetDirectoryMember { UserReference = "LD0/GGIO1.Ind2.stVal", FunctionalConstraint = "ST" }
        };

        var frame = MmsReportFrameMapper.Map(decoded, members, DateTimeOffset.UnixEpoch);

        Assert.Equal("optflds-driven", frame.DecoderMode);
        Assert.Empty(frame.ParseWarnings);
        Assert.Equal("LD0/LLN0$BR$brcbA01", frame.Header.ReportId);
        Assert.Equal("LD0/LLN0$DataSet", frame.Header.DataSetReference);
        Assert.Equal((ulong)1, frame.Header.ConfRev);
        Assert.Equal((ulong)0, frame.Header.SequenceNumber);
        Assert.False(frame.Header.BufferOverflow);
        Assert.Equal("0000000000000014", frame.Header.EntryIdHex);
        Assert.Equal([0, 1], frame.IncludedDataSetIndexes);
        Assert.Equal(2, frame.Values.Count);
        Assert.All(frame.Values, value => Assert.Equal(["general-interrogation"], value.ReasonForInclusion));
    }
}
