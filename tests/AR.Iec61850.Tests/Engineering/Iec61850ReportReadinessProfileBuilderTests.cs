using AR.Iec61850.Engineering;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Engineering;

public sealed class Iec61850ReportReadinessProfileBuilderTests
{
    [Fact]
    public void BuildStatic_ReturnsGuardedReadyProfile_WhenStaticDatasetAndFreeRcbExist()
    {
        var discovery = CreateSafeDiscovery();
        var directory = CreateDataSetDirectory("IED1LD0/LLN0.dsStatus", 2);

        var profile = Iec61850ReportReadinessProfileBuilder.BuildStatic(
            discovery,
            [directory],
            new Iec61850ReportReadinessProfileOptions
            {
                Host = "192.0.2.10",
                Port = 102,
                TriggerGeneralInterrogation = true,
                ListenDurationSeconds = 45
            });

        Assert.True(profile.IsReadyForGuardedLiveSession);
        Assert.NotNull(profile.SessionProfile);
        Assert.Equal("IED1LD0/LLN0.RP.rpt01", profile.SessionProfile!.ReportControlReference);
        Assert.Equal(2, profile.SessionProfile.Members.Count);
        Assert.Contains(profile.AcceptanceGates, x => x.Code == "LIVE_WRITE_GATE" && x.Severity == Iec61850DiagnosticSeverity.Warning);
        Assert.Contains(profile.Candidates, x => x.Reference == "IED1LD0/LLN0.RP.rpt01" && x.Safety == Iec61850ReportCandidateSafety.Preferred);
        Assert.Contains(profile.Diagnostics, x => x.Code == "REPORT_STATIC_PROFILE_READY" && x.Severity == Iec61850DiagnosticSeverity.Info);
    }

    [Fact]
    public void BuildStatic_ReturnsBlockedProfile_WhenDatasetDirectoryIsMissing()
    {
        var discovery = CreateSafeDiscovery();

        var profile = Iec61850ReportReadinessProfileBuilder.BuildStatic(
            discovery,
            Array.Empty<MmsDataSetDirectoryResult>(),
            new Iec61850ReportReadinessProfileOptions { Host = "192.0.2.10" });

        Assert.False(profile.IsReadyForGuardedLiveSession);
        Assert.Contains(profile.AcceptanceGates, x => x.Code == "DATASET_DIRECTORY_GATE" && x.Severity == Iec61850DiagnosticSeverity.Error);
        Assert.Contains(profile.Diagnostics, x => x.Code == "REPORT_STATIC_PROFILE_BLOCKED" && x.Severity == Iec61850DiagnosticSeverity.Error);
        Assert.Contains(profile.Diagnostics, x => x.Code == "REPORT_PLAN_BLOCKER");
    }

    [Fact]
    public void BuildStatic_FlagsOccupiedAndReservedCandidates()
    {
        var discovery = CreateDiscoveryWithOccupiedAndReservedRcbs();
        var directory = CreateDataSetDirectory("IED1LD0/LLN0.dsStatus", 1);

        var profile = Iec61850ReportReadinessProfileBuilder.BuildStatic(
            discovery,
            [directory],
            new Iec61850ReportReadinessProfileOptions { Host = "192.0.2.10" });

        Assert.False(profile.IsReadyForGuardedLiveSession);
        Assert.Contains(profile.Candidates, x => x.Reference == "IED1LD0/LLN0.RP.occupied" && x.Safety == Iec61850ReportCandidateSafety.Blocked);
        Assert.Contains(profile.Candidates, x => x.Reference == "IED1LD0/LLN0.BR.reserved" && x.Safety == Iec61850ReportCandidateSafety.Blocked);
        Assert.Contains(profile.Diagnostics, x => x.Code == "RCB_OCCUPIED_COUNT" && x.Severity == Iec61850DiagnosticSeverity.Warning);
        Assert.Contains(profile.Diagnostics, x => x.Code == "RCB_RESERVED_COUNT" && x.Severity == Iec61850DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ToMarkdown_EmitsAcceptanceGatesPlanAndCandidateMatrix()
    {
        var profile = Iec61850ReportReadinessProfileBuilder.BuildStatic(
            CreateSafeDiscovery(),
            [CreateDataSetDirectory("IED1LD0/LLN0.dsStatus", 1)],
            new Iec61850ReportReadinessProfileOptions { Host = "192.0.2.10" });

        var markdown = profile.ToMarkdown();

        Assert.Contains("# ARIEC61850 Report Readiness Profile", markdown, StringComparison.Ordinal);
        Assert.Contains("## Acceptance gates", markdown, StringComparison.Ordinal);
        Assert.Contains("## Selected static report plan", markdown, StringComparison.Ordinal);
        Assert.Contains("## RCB candidate matrix", markdown, StringComparison.Ordinal);
        Assert.Contains("REPORT_STATIC_PROFILE_READY", markdown, StringComparison.Ordinal);
    }

    private static MmsDiscoveryResult CreateSafeDiscovery()
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
            TriggerOptions = "dchg qchg dupd GI",
            OptionalFields = "seqNum timeStamp reasonCode dataSet confRev",
            Status = "Attribute-probed"
        });

        return new MmsDiscoveryResult
        {
            IedDirectory = CreateDirectory(),
            ReportInventory = inventory,
            Summary = "synthetic discovery"
        };
    }

    private static MmsDiscoveryResult CreateDiscoveryWithOccupiedAndReservedRcbs()
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
            Name = "occupied",
            Reference = "IED1LD0/LLN0.RP.occupied",
            Buffered = false,
            DataSetReference = "IED1LD0/LLN0.dsStatus",
            EnabledState = "true",
            ReservationState = "false",
            Status = "Attribute-probed"
        });
        inventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "IED1LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "BR",
            Name = "reserved",
            Reference = "IED1LD0/LLN0.BR.reserved",
            Buffered = true,
            DataSetReference = "IED1LD0/LLN0.dsStatus",
            EnabledState = "false",
            ReservationTimeSeconds = "30",
            Status = "Attribute-probed"
        });

        return new MmsDiscoveryResult
        {
            IedDirectory = CreateDirectory(),
            ReportInventory = inventory,
            Summary = "synthetic discovery"
        };
    }

    private static MmsIedModelDirectory CreateDirectory()
        => new(
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
            }
        ]);

    private static MmsDataSetDirectoryResult CreateDataSetDirectory(string reference, int memberCount)
        => new()
        {
            IsSuccess = true,
            DataSetReference = reference,
            Members = Enumerable.Range(0, memberCount)
                .Select(index => new MmsDataSetDirectoryMember
                {
                    Domain = "IED1LD0",
                    MmsItemName = index == 0 ? "XCBR1$ST$Pos$stVal" : "MMXU1$MX$PhV$phsA$cVal$mag$f",
                    UserReference = index == 0 ? "IED1LD0/XCBR1.Pos.stVal" : "IED1LD0/MMXU1.PhV.phsA.cVal.mag.f",
                    FunctionalConstraint = index == 0 ? "ST" : "MX",
                    LogicalNode = index == 0 ? "XCBR1" : "MMXU1",
                    DataObjectPath = index == 0 ? "Pos.stVal" : "PhV.phsA.cVal.mag.f",
                    Confidence = 100
                })
                .ToArray()
        };
}
