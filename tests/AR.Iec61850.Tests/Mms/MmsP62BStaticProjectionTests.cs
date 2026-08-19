using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsP62BStaticProjectionTests
{
    [Fact]
    public void Project_FieldMxPair_MapsInstMagAndMagWithSharedQualityTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 8, 19, 2, 8, 21, TimeSpan.Zero);
        var projection = MmsReportValueProjector.Project(Frame(
            "IEDLD/TTMP1.WidTmpU",
            MmsDataValue.Structure([
                MmsDataValue.Structure([MmsDataValue.Integer(32766)]),
                MmsDataValue.Structure([MmsDataValue.Integer(123)]),
                MmsDataValue.BitString(3, [0x00, 0x00]),
                MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0))
            ]),
            timestamp));

        Assert.Empty(projection.Warnings);
        Assert.Contains(projection.Updates, update =>
            update.Reference == "IEDLD/TTMP1.WidTmpU.instMag.f" &&
            update.Value == "32766" &&
            update.Quality == "good" &&
            update.ProjectionStatus == "projected-mx-pair");
        Assert.Contains(projection.Updates, update =>
            update.Reference == "IEDLD/TTMP1.WidTmpU.mag.f" &&
            update.Value == "123" &&
            update.Quality == "good" &&
            update.Timestamp.Contains("2026-08-19", StringComparison.Ordinal));
        Assert.DoesNotContain(projection.Updates, update => update.Value.StartsWith("Structure(", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Project_FieldComplexPair_MapsInstCValAndCValMagnitudeWithoutChoosingOne()
    {
        var timestamp = new DateTimeOffset(2026, 8, 19, 2, 8, 22, TimeSpan.Zero);
        var projection = MmsReportValueProjector.Project(Frame(
            "IEDLD/MMXU1.A.phsA",
            MmsDataValue.Structure([
                MmsDataValue.Structure([MmsDataValue.Structure([MmsDataValue.Integer(11)])]),
                MmsDataValue.Structure([MmsDataValue.Structure([MmsDataValue.Integer(22)])]),
                MmsDataValue.BitString(3, [0x00, 0x00]),
                MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0))
            ]),
            timestamp));

        Assert.Empty(projection.Warnings);
        Assert.Contains(projection.Updates, update =>
            update.Reference == "IEDLD/MMXU1.A.phsA.instCVal.mag.f" && update.Value == "11");
        Assert.Contains(projection.Updates, update =>
            update.Reference == "IEDLD/MMXU1.A.phsA.cVal.mag.f" && update.Value == "22");
    }

    [Fact]
    public void Project_AmbiguousSixFieldVendorStruct_RemainsRawAndFailClosed()
    {
        var timestamp = new DateTimeOffset(2026, 8, 19, 2, 8, 23, TimeSpan.Zero);
        var projection = MmsReportValueProjector.Project(Frame(
            "IEDLD/GGIO2.CBClsCmdRecv",
            MmsDataValue.Structure([
                MmsDataValue.Structure([MmsDataValue.Integer(8), MmsDataValue.Integer(0)]),
                MmsDataValue.Integer(0),
                MmsDataValue.Boolean(false),
                MmsDataValue.BitString(3, [0x00, 0x00]),
                MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0)),
                MmsDataValue.Integer(0)
            ]),
            timestamp));

        var update = Assert.Single(projection.Updates);
        Assert.Equal("IEDLD/GGIO2.CBClsCmdRecv", update.Reference);
        Assert.StartsWith("Structure(", update.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(projection.Warnings, warning => warning.Contains("REPORT_RAW_STRUCT", StringComparison.OrdinalIgnoreCase));
        Assert.False(update.IsProjectedChild);
    }

    private static MmsReportFrame Frame(string reference, MmsDataValue value, DateTimeOffset timestamp)
        => new()
        {
            ReceivedAt = timestamp,
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = reference,
                        FunctionalConstraint = "MX"
                    },
                    Value = value,
                    ReasonForInclusion = ["general-interrogation"]
                }
            ]
        };
}
