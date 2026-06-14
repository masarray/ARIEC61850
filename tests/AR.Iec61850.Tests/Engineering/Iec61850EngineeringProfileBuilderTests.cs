using AR.Iec61850.Engineering;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Engineering;

public sealed class Iec61850EngineeringProfileBuilderTests
{
    [Fact]
    public void Build_ClassifiesReportLabReady_WhenModelDatasetAndSafeRcbExist()
    {
        var discovery = CreateDiscoveryWithSafeReportCandidate();
        var dataSetDirectory = new MmsDataSetDirectoryResult
        {
            IsSuccess = true,
            DataSetReference = "IED1LD0/LLN0.dsStatus",
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "IED1LD0",
                    MmsItemName = "XCBR1$ST$Pos$stVal",
                    UserReference = "IED1LD0/XCBR1.Pos.stVal",
                    FunctionalConstraint = "ST",
                    LogicalNode = "XCBR1",
                    DataObjectPath = "Pos.stVal"
                }
            ]
        };

        var profile = Iec61850EngineeringProfileBuilder.Build(
            discovery,
            [dataSetDirectory],
            new Iec61850EngineeringProfileOptions { Host = "192.0.2.10", Port = 102 });

        Assert.True(profile.HasUsableModel);
        Assert.True(profile.HasReportPathCandidate);
        Assert.True(profile.IsReportLabReady);
        Assert.Equal(1, profile.SafeReportCandidateCount);
        Assert.Contains(profile.Diagnostics, x => x.Code == "RCB_SAFE_CANDIDATE" && x.Reference == "IED1LD0/LLN0.RP.rpt01");
        Assert.Contains(profile.Capabilities, x => x.Area == "Report service" && x.Status == Iec61850CapabilityStatus.Ready);
        Assert.Contains("reportLabReady=true", profile.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FlagsMissingDatasetAndUnprobedRcb_AsEngineBlockers()
    {
        var inventory = new MmsReportInventory();
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = "rpt01",
            Reference = "IED1LD0/LLN0.RP.rpt01",
            Buffered = false,
            Status = "Discovered"
        });

        var discovery = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "XCBR1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Pos.stVal",
                    MmsItemName = "XCBR1$ST$Pos$stVal"
                }
            ]),
            ReportInventory = inventory,
            Summary = "synthetic discovery"
        };

        var profile = Iec61850EngineeringProfileBuilder.Build(discovery);

        Assert.False(profile.IsReportLabReady);
        Assert.Contains(profile.Diagnostics, x => x.Code == "DATASET_NONE_DISCOVERED" && x.Severity == Iec61850DiagnosticSeverity.Warning);
        Assert.Contains(profile.Diagnostics, x => x.Code == "RCB_NO_SAFE_CANDIDATE" && x.Severity == Iec61850DiagnosticSeverity.Warning);
        Assert.Contains(profile.Capabilities, x => x.Area == "Report service" && x.Status == Iec61850CapabilityStatus.Partial);
    }

    [Fact]
    public void ToMarkdown_EmitsCapabilityAndDiagnosticTables()
    {
        var profile = Iec61850EngineeringProfileBuilder.Build(CreateDiscoveryWithSafeReportCandidate());

        var markdown = profile.ToMarkdown();

        Assert.Contains("# ARIEC61850 Engineering Profile", markdown, StringComparison.Ordinal);
        Assert.Contains("| Area | Status | Evidence | Next action |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Severity | Code | Reference | Message | Recommendation |", markdown, StringComparison.Ordinal);
        Assert.Contains("RCB_SAFE_CANDIDATE", markdown, StringComparison.Ordinal);
    }

    private static MmsDiscoveryResult CreateDiscoveryWithSafeReportCandidate()
    {
        var inventory = new MmsReportInventory();
        inventory.DataSets.Add(new MmsDataSetCandidate
        {
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            Name = "dsStatus",
            Reference = "IED1LD0/LLN0.dsStatus",
            RawMmsName = "LLN0$dsStatus"
        });
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = "rpt01",
            Reference = "IED1LD0/LLN0.RP.rpt01",
            Buffered = false,
            DataSetReference = "IED1LD0/LLN0.dsStatus",
            EnabledState = "false",
            ReservationState = "false",
            ReportId = "IED1LD0/LLN0$RP$rpt01",
            ConfRev = "1",
            Status = "Attribute-probed"
        });

        return new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "XCBR1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Pos.stVal",
                    MmsItemName = "XCBR1$ST$Pos$stVal"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "MMXU1",
                    FunctionalConstraint = "MX",
                    DataObjectPath = "PhV.phsA.cVal.mag.f",
                    MmsItemName = "MMXU1$MX$PhV$phsA$cVal$mag$f"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "CSWI1",
                    FunctionalConstraint = "CO",
                    DataObjectPath = "Pos.Oper.ctlVal",
                    MmsItemName = "CSWI1$CO$Pos$Oper$ctlVal"
                }
            ]),
            ReportInventory = inventory,
            Summary = "synthetic discovery"
        };
    }
}
