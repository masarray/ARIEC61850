using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class LiveRcbDataSetEvidenceMergerTests
{
    [Fact]
    public void MergeSelectedDataSetDirectory_Replaces_Incomplete_Members_Without_Mutating_Source()
    {
        var dataSet = new LiveIedDataSetModel
        {
            Reference = "IED1LD0/LLN0.DataSet",
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            Name = "DataSet",
            MemberCount = 1,
            Members = Array.Empty<LiveIedDataSetMemberModel>()
        };
        var reportControl = new LiveIedReportControlModel
        {
            Reference = "IED1LD0/LLN0.RP.URCB01",
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            Name = "URCB01",
            Buffered = false,
            DataSetReference = dataSet.Reference
        };
        var source = new LiveIedModelDiscoveryDocument
        {
            IedName = "IED1",
            DataSets = new[] { dataSet },
            ReportControls = new[] { reportControl },
            Coverage = new LiveIedModelDiscoveryCoverage
            {
                DataSetCount = 1,
                ReportControlCount = 1,
                UnbufferedReportControlCount = 1
            }
        };
        var availability = new MmsRcbAvailabilityResult
        {
            ReportControls = new[]
            {
                new MmsRcbAvailabilitySnapshot
                {
                    Reference = reportControl.Reference,
                    DataSetReference = dataSet.Reference,
                    DataSetDirectoryRead = true,
                    DataSetDirectorySuccess = true,
                    DataSetMemberCount = 1,
                    DataSetMembers = new[]
                    {
                        new MmsDataSetDirectoryMember
                        {
                            Domain = "IED1LD0",
                            MmsItemName = "XCBR1$ST$Pos$stVal",
                            UserReference = "IED1LD0/XCBR1.Pos.stVal",
                            FunctionalConstraint = "ST"
                        }
                    }
                }
            }
        };

        var merged = LiveRcbDataSetEvidenceMerger.MergeSelectedDataSetDirectory(
            source,
            reportControl.Reference,
            availability);
        var filtered = SclReportControlFilter.FilterLiveModel(merged, reportControl.Reference);

        var mergedDataSet = Assert.Single(filtered.DataSets);
        var member = Assert.Single(mergedDataSet.Members);
        Assert.Equal("IED1LD0/XCBR1.Pos.stVal", member.Reference);
        Assert.Equal("ST", member.FunctionalConstraint);
        Assert.Empty(source.DataSets[0].Members);
        Assert.Single(filtered.ReportControls);
    }

    [Fact]
    public void MergeSelectedDataSetDirectory_Requires_Check_Availability_Evidence()
    {
        var source = new LiveIedModelDiscoveryDocument
        {
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = "IED1LD0/LLN0.DataSet",
                    Domain = "IED1LD0",
                    LogicalNode = "LLN0",
                    Name = "DataSet",
                    MemberCount = 1
                }
            },
            ReportControls = new[]
            {
                new LiveIedReportControlModel
                {
                    Reference = "IED1LD0/LLN0.RP.URCB01",
                    Domain = "IED1LD0",
                    LogicalNode = "LLN0",
                    Name = "URCB01",
                    DataSetReference = "IED1LD0/LLN0.DataSet"
                }
            }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            LiveRcbDataSetEvidenceMerger.MergeSelectedDataSetDirectory(
                source,
                "IED1LD0/LLN0.RP.URCB01",
                new MmsRcbAvailabilityResult()));

        Assert.Contains("Run Check Availability", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
