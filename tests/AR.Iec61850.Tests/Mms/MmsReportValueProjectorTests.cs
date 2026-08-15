using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportValueProjectorTests
{
    [Fact]
    public void Project_Expands_Acd_Str_Struct_To_Engineer_Readable_Signals()
    {
        var timestamp = new DateTimeOffset(2026, 6, 13, 14, 58, 23, TimeSpan.Zero);
        var frame = new MmsReportFrame
        {
            ReceivedAt = timestamp,
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = "LD0/A50PTOC1.Str",
                        FunctionalConstraint = "ST"
                    },
                    Value = MmsDataValue.Structure([
                        MmsDataValue.Boolean(true),
                        MmsDataValue.Integer(0),
                        MmsDataValue.Boolean(true),
                        MmsDataValue.Integer(0),
                        MmsDataValue.Boolean(false),
                        MmsDataValue.Integer(0),
                        MmsDataValue.Boolean(false),
                        MmsDataValue.Integer(0),
                        MmsDataValue.BitString(3, [0x00, 0x00]),
                        MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0))
                    ]),
                    ReasonForInclusion = ["general-interrogation"]
                }
            ]
        };

        var projection = MmsReportValueProjector.Project(frame);

        Assert.Empty(projection.Warnings);
        Assert.Contains(projection.Updates, x => x.Reference == "LD0/A50PTOC1.Str.general" && x.Value == "true" && x.Quality == "good");
        Assert.Contains(projection.Updates, x => x.Reference == "LD0/A50PTOC1.Str.phsA" && x.Value == "true" && x.Timestamp.Contains("2026-06-13", StringComparison.Ordinal));
        Assert.DoesNotContain(projection.Updates, x => x.Value.StartsWith("Struct(", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Project_Merges_Leaf_StVal_With_Separate_Q_And_T_Report_Members()
    {
        var timestamp = new DateTimeOffset(2026, 6, 13, 14, 58, 23, TimeSpan.Zero);
        var frame = new MmsReportFrame
        {
            ReceivedAt = timestamp,
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember { UserReference = "LD0/XCBR1.Pos.stVal", FunctionalConstraint = "ST" },
                    Value = MmsDataValue.Integer(1),
                    ReasonForInclusion = ["data-change"]
                },
                new MmsReportValue
                {
                    Index = 1,
                    Member = new MmsDataSetDirectoryMember { UserReference = "LD0/XCBR1.Pos.q", FunctionalConstraint = "ST" },
                    Value = MmsDataValue.BitString(3, [0x00, 0x00]),
                    ReasonForInclusion = ["quality-change"]
                },
                new MmsReportValue
                {
                    Index = 2,
                    Member = new MmsDataSetDirectoryMember { UserReference = "LD0/XCBR1.Pos.t", FunctionalConstraint = "ST" },
                    Value = MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0)),
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };

        var update = Assert.Single(MmsReportValueProjector.Project(frame).Updates);
        Assert.Equal("LD0/XCBR1.Pos.stVal", update.Reference);
        Assert.Equal("off", update.Value);
        Assert.Equal("good", update.Quality);
        Assert.Contains("2026-06-13", update.Timestamp, StringComparison.Ordinal);
        Assert.Equal("report", update.Source);
    }

    [Fact]
    public void Project_Preserves_Q_Only_Report_As_A_Partial_Companion_Update()
    {
        var frame = new MmsReportFrame
        {
            ReceivedAt = DateTimeOffset.UtcNow,
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember { UserReference = "LD0/XCBR1.Pos.q", FunctionalConstraint = "ST" },
                    Value = MmsDataValue.BitString(3, [0x00, 0x00]),
                    ReasonForInclusion = ["quality-change"]
                }
            ]
        };

        var update = Assert.Single(MmsReportValueProjector.Project(frame).Updates);

        Assert.Equal("LD0/XCBR1.Pos", update.Reference);
        Assert.False(update.HasValue);
        Assert.True(update.HasQuality);
        Assert.False(update.HasTimestamp);
        Assert.Equal("companion-only", update.ProjectionStatus);
    }

    [Fact]
    public void Project_Expands_Boolean_Status_Struct_To_StVal_With_Quality_And_Timestamp()
    {
        var timestamp = new DateTimeOffset(2026, 8, 15, 16, 11, 57, TimeSpan.Zero);
        var frame = new MmsReportFrame
        {
            ReceivedAt = timestamp,
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = "INVERTERA2/LLN0.ACAlm1",
                        FunctionalConstraint = "ST"
                    },
                    Value = MmsDataValue.Structure([
                        MmsDataValue.Boolean(false),
                        MmsDataValue.BitString(3, [0x00, 0x00]),
                        MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0))
                    ]),
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };

        var projection = MmsReportValueProjector.Project(frame);
        var update = Assert.Single(projection.Updates);

        Assert.Empty(projection.Warnings);
        Assert.Equal("INVERTERA2/LLN0.ACAlm1.stVal", update.Reference);
        Assert.Equal("false", update.Value);
        Assert.Equal("good", update.Quality);
        Assert.Contains("2026-08-15", update.Timestamp, StringComparison.Ordinal);
        Assert.True(update.HasValue);
        Assert.True(update.HasQuality);
        Assert.True(update.HasTimestamp);
        Assert.True(update.IsProjectedChild);
        Assert.Equal("projected-boolean-status", update.ProjectionStatus);
    }

    [Fact]
    public void Project_Does_Not_Misclassify_NonBoolean_Three_Field_Struct_As_Status()
    {
        var timestamp = new DateTimeOffset(2026, 8, 15, 16, 11, 57, TimeSpan.Zero);
        var frame = new MmsReportFrame
        {
            ReceivedAt = timestamp,
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = "LD0/MMXU1.CustomValue",
                        FunctionalConstraint = "MX"
                    },
                    Value = MmsDataValue.Structure([
                        MmsDataValue.Integer(123),
                        MmsDataValue.BitString(3, [0x00, 0x00]),
                        MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0))
                    ]),
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };

        var projection = MmsReportValueProjector.Project(frame);
        var update = Assert.Single(projection.Updates);

        Assert.Equal("LD0/MMXU1.CustomValue", update.Reference);
        Assert.StartsWith("Struct(", update.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(projection.Warnings, warning => warning.Contains("REPORT_RAW_STRUCT", StringComparison.OrdinalIgnoreCase));
        Assert.False(update.IsProjectedChild);
        Assert.Equal("direct", update.ProjectionStatus);
    }

    [Theory]
    [InlineData(0x40, "off")]
    [InlineData(0x80, "on")]
    public void Project_Decodes_TwoBit_Dbpos_Report_For_Both_Directions(byte encoded, string expected)
    {
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
                        UserReference = "LD0/XCBR1.Pos.stVal",
                        FunctionalConstraint = "ST"
                    },
                    Value = MmsDataValue.BitString(6, [encoded]),
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };

        var update = Assert.Single(MmsReportValueProjector.Project(frame).Updates);

        Assert.Equal("LD0/XCBR1.Pos.stVal", update.Reference);
        Assert.Equal(expected, update.Value);
        Assert.True(update.HasValue);
        Assert.Equal("data-change", update.Reason);
    }

}
