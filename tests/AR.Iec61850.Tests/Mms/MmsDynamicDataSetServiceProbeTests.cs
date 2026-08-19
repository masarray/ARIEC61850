using System.Text;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDynamicDataSetServiceProbeTests
{
    [Fact]
    public void ProbePolicy_StaticPlan_IsNeverProbed()
    {
        var plan = new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = BuildUrcb(),
            DataSetReference = "LD0/LLN0.Static",
            DynamicPoints = [BuildPoint()]
        };

        Assert.False(MmsDynamicDataSetProbePolicy.ShouldProbe(plan));
    }

    [Fact]
    public void ProbePolicy_ReadyDynamicPlan_WithResolvedPoint_IsProbed()
    {
        var plan = new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.DynamicDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = BuildUrcb(),
            DataSetReference = "LD0/LLN0.AR_HYB_01",
            DynamicPoints = [BuildPoint()]
        };

        Assert.True(MmsDynamicDataSetProbePolicy.ShouldProbe(plan));
    }

    [Fact]
    public void ProbePolicy_BlockedDynamicPlan_IsNotProbed()
    {
        var plan = new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.DynamicDataSet,
            Status = MmsReportSubscriptionPlanStatus.Blocked,
            ReportControl = BuildUrcb(),
            DataSetReference = "LD0/LLN0.AR_HYB_01",
            DynamicPoints = [BuildPoint()]
        };

        Assert.False(MmsDynamicDataSetProbePolicy.ShouldProbe(plan));
    }

    [Fact]
    public void ProbePolicy_DynamicPlanWithoutDataSetReference_IsNotProbed()
    {
        var plan = new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.DynamicDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = BuildUrcb(),
            DataSetReference = string.Empty,
            DynamicPoints = [BuildPoint()]
        };

        Assert.False(MmsDynamicDataSetProbePolicy.ShouldProbe(plan));
    }

    [Fact]
    public void ProbeResult_DefineFailed_NeedsNoCleanup()
    {
        var result = new MmsDynamicDataSetProbeResult
        {
            DirectoryAttempted = false,
            DefineEvidence = new MmsDynamicDataSetProbeServiceEvidence
            {
                Attempted = true,
                IsSuccess = false,
                StateBefore = MmsAssociationState.MmsInitiated,
                StateAfter = MmsAssociationState.MmsInitiateFailed
            }
        };

        Assert.False(result.CleanupAttempted);
        Assert.True(result.CleanupSucceeded);
        Assert.False(result.AssociationSurvived);
    }

    [Fact]
    public void ProbeResult_DefineSucceededButVerificationLostAssociation_FailsCleanupClosed()
    {
        var result = new MmsDynamicDataSetProbeResult
        {
            DirectoryAttempted = true,
            DefineEvidence = new MmsDynamicDataSetProbeServiceEvidence
            {
                Attempted = true,
                IsSuccess = true,
                StateBefore = MmsAssociationState.MmsInitiated,
                StateAfter = MmsAssociationState.MmsInitiated
            },
            DeleteEvidence = new MmsDynamicDataSetProbeServiceEvidence
            {
                Attempted = false,
                IsSuccess = false,
                StateBefore = MmsAssociationState.MmsInitiateFailed,
                StateAfter = MmsAssociationState.MmsInitiateFailed
            }
        };

        Assert.False(result.CleanupAttempted);
        Assert.False(result.CleanupSucceeded);
        Assert.False(result.AssociationSurvived);
    }

    [Fact]
    public void SingleMemberDefineRequest_IsDeterministicAndCarriesExactNames()
    {
        var member = new MmsObjectReference("LD0", "GGIO1$ST$Ind1$stVal", "ST");

        var first = MmsDefineNamedVariableListRequest.Build(
            17,
            "LD0/LLN0.AR_HYB_01",
            [member]);
        var second = MmsDefineNamedVariableListRequest.Build(
            17,
            "LD0/LLN0.AR_HYB_01",
            [member]);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
        Assert.True(ContainsAscii(first, "LD0"));
        Assert.True(ContainsAscii(first, "LLN0$AR_HYB_01"));
        Assert.True(ContainsAscii(first, "GGIO1$ST$Ind1$stVal"));
    }

    [Theory]
    [InlineData(MmsDynamicDataSetProbeFailureStage.DefineNamedVariableList, MmsReportActivationFailureReason.DynamicDataSetProbeDefineFailed)]
    [InlineData(MmsDynamicDataSetProbeFailureStage.GetNamedVariableListAttributes, MmsReportActivationFailureReason.DynamicDataSetProbeVerificationFailed)]
    [InlineData(MmsDynamicDataSetProbeFailureStage.DeleteNamedVariableList, MmsReportActivationFailureReason.DynamicDataSetProbeDeleteFailed)]
    public void ProbeFailureStage_MapsToExactActivationReason(
        MmsDynamicDataSetProbeFailureStage stage,
        MmsReportActivationFailureReason expected)
    {
        Assert.Equal(expected, MmsDynamicDataSetProbePolicy.FailureReason(stage));
    }

    private static MmsReportControlCandidate BuildUrcb()
        => new()
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = "urcb01",
            Reference = "LD0/LLN0.RP.urcb01",
            Buffered = false,
            EnabledState = "false",
            Attributes = ["RptEna", "DatSet", "TrgOps", "OptFlds", "Resv", "GI"]
        };

    private static MmsFcResolvedPoint BuildPoint()
        => new()
        {
            Domain = "LD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "ST",
            DataObjectPath = "Ind1.stVal",
            MmsItemName = "GGIO1$ST$Ind1$stVal"
        };

    private static bool ContainsAscii(byte[] source, string text)
    {
        var needle = Encoding.ASCII.GetBytes(text);
        if (needle.Length == 0 || source.Length < needle.Length)
            return false;

        for (var offset = 0; offset <= source.Length - needle.Length; offset++)
        {
            var match = true;
            for (var index = 0; index < needle.Length; index++)
            {
                if (source[offset + index] == needle[index])
                    continue;
                match = false;
                break;
            }

            if (match)
                return true;
        }

        return false;
    }
}
