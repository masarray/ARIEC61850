using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportZeroOptionalFieldsRegressionTests
{
    [Fact]
    public void ZeroOptFlds_FullDigitalReport_MapsOnlyProcessValues()
    {
        var items = new List<MmsInformationReportItem>
        {
            new() { Index = 0, Value = MmsDataValue.VisibleString("AA1C1F13R4Application/LLN0$RP$Unbuffer01") },
            // OptFlds is a valid 10-bit IEC 61850 field even when every optional bit is zero.
            new() { Index = 1, Value = MmsDataValue.BitString(6, [0x00, 0x00]) },
            // Inclusion bitmap: all 36 members included (40 storage bits, 4 unused).
            new() { Index = 2, Value = MmsDataValue.BitString(4, [0xFF, 0xFF, 0xFF, 0xFF, 0xF0]) }
        };

        for (var index = 0; index < 36; index++)
        {
            MmsDataValue value = index switch
            {
                0 => MmsDataValue.Boolean(false),
                1 => MmsDataValue.BitString(6, [0x40]), // DPC Open [01]
                _ => MmsDataValue.Boolean((index & 1) != 0)
            };
            items.Add(new MmsInformationReportItem { Index = items.Count, Value = value });
        }

        var decoded = new MmsInformationReport
        {
            IsSuccess = true,
            Items = items,
            Message = "field-shaped zero-OptFlds report"
        };
        var members = Enumerable.Range(0, 36)
            .Select(index => new MmsDataSetDirectoryMember
            {
                UserReference = index switch
                {
                    0 => "AA1C1F13R4DSQZ1/CILO1.EnaOpn.stVal",
                    1 => "AA1C1F13R4DSQZ1/CSWI1.Pos.stVal",
                    _ => $"AA1C1F13R4ADD/GGIO1.Ind{index}.stVal"
                },
                FunctionalConstraint = "ST"
            })
            .ToArray();

        var frame = MmsReportFrameMapper.Map(decoded, members, DateTimeOffset.UnixEpoch);
        var projection = MmsReportValueProjector.Project(frame);

        Assert.Equal("optflds-driven", frame.DecoderMode);
        Assert.Empty(frame.ParseWarnings);
        Assert.Equal(2, frame.InclusionBitstringItemIndex);
        Assert.Equal(36, frame.Values.Count);
        Assert.Equal(Enumerable.Range(0, 36), frame.IncludedDataSetIndexes);
        Assert.Equal(MmsDataKind.Boolean, frame.Values[0].Value!.Kind);
        Assert.False(Assert.IsType<bool>(frame.Values[0].Value!.Value));
        Assert.Equal(MmsDataKind.BitString, frame.Values[1].Value!.Kind);
        Assert.Equal("AA1C1F13R4DSQZ1/CILO1.EnaOpn.stVal", frame.Values[0].MemberReference);
        Assert.Equal("AA1C1F13R4DSQZ1/CSWI1.Pos.stVal", frame.Values[1].MemberReference);
        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals("AA1C1F13R4DSQZ1/CILO1.EnaOpn.stVal", StringComparison.OrdinalIgnoreCase) &&
            update.Value.Equals("false", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals("AA1C1F13R4DSQZ1/CSWI1.Pos.stVal", StringComparison.OrdinalIgnoreCase) &&
            update.Value.Contains("Open", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projection.Updates, update =>
            update.Value.StartsWith("bits(", StringComparison.OrdinalIgnoreCase) &&
            !update.Reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CanonicalReportThatCannotBeMapped_QuarantinesRawAccessResults()
    {
        var decoded = new MmsInformationReport
        {
            IsSuccess = true,
            Items =
            [
                new MmsInformationReportItem { Index = 0, Value = MmsDataValue.VisibleString("LD0/LLN0$RP$urcb01") },
                new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(6, [0x00, 0x00]) },
                // Wrong inclusion length for a four-member DataSet. This frame must be rejected,
                // not projected by raw AccessResult index.
                new MmsInformationReportItem { Index = 2, Value = MmsDataValue.BitString(7, [0x80]) },
                new MmsInformationReportItem { Index = 3, Value = MmsDataValue.Boolean(true) }
            ],
            Message = "malformed canonical report"
        };
        var members = Enumerable.Range(0, 4)
            .Select(index => new MmsDataSetDirectoryMember
            {
                UserReference = $"LD0/GGIO1.Ind{index}.stVal",
                FunctionalConstraint = "ST"
            })
            .ToArray();

        var frame = MmsReportFrameMapper.Map(decoded, members, DateTimeOffset.UnixEpoch);

        Assert.Equal("rejected-unmapped", frame.DecoderMode);
        Assert.Empty(frame.Values);
        Assert.Contains(frame.ParseWarnings, warning => warning.Contains("REPORT_FRAME_REJECTED", StringComparison.Ordinal));
        Assert.Equal("LD0/LLN0$RP$urcb01", frame.Header.ReportId);
    }

    [Fact]
    public void ReasonForInclusion_BitOne_IsDataChange()
    {
        var decoded = new MmsInformationReport
        {
            IsSuccess = true,
            Items =
            [
                new MmsInformationReportItem { Index = 0, Value = MmsDataValue.VisibleString("LD0/LLN0$RP$urcb01") },
                // OptFlds bit 3 = reason-for-inclusion.
                new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(6, [0x10, 0x00]) },
                new MmsInformationReportItem { Index = 2, Value = MmsDataValue.BitString(7, [0x80]) },
                new MmsInformationReportItem { Index = 3, Value = MmsDataValue.Boolean(true) },
                // Reason bit 0 is reserved; bit 1 is data-change.
                new MmsInformationReportItem { Index = 4, Value = MmsDataValue.BitString(2, [0x40]) }
            ],
            Message = "reason test"
        };
        var members = new[]
        {
            new MmsDataSetDirectoryMember { UserReference = "LD0/GGIO1.Ind1.stVal", FunctionalConstraint = "ST" }
        };

        var frame = MmsReportFrameMapper.Map(decoded, members, DateTimeOffset.UnixEpoch);

        var value = Assert.Single(frame.Values);
        Assert.Equal(["data-change"], value.ReasonForInclusion);
    }
}
