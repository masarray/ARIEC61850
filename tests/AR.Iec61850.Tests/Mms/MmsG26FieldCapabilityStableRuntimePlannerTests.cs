using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsG26FieldCapabilityStableRuntimePlannerTests
{
    [Fact]
    public void SameRcb_KeepsSameDynamicDataSetIdentity_AcrossFullAndIsolatedRevalidation()
    {
        var full = MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner.WithStableDynamicDataSetIdentities(
            CapabilityPlan(
                DynamicSegment("LD0/LLN0.RP.U01", "LD0/LLN0.AR_HYB_01", "LD0/GGIO1$ST$Ind1$stVal"),
                DynamicSegment("LD0/LLN0.RP.U02", "LD0/LLN0.AR_HYB_02", "LD0/GGIO1$ST$Ind2$stVal")));

        var isolatedSecond = MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner.WithStableDynamicDataSetIdentities(
            CapabilityPlan(
                DynamicSegment("LD0/LLN0.RP.U02", "LD0/LLN0.AR_HYB_01", "LD0/GGIO1$ST$Ind2$stVal")));

        var fullSegments = full.AcquisitionPlan.Segments.ToArray();
        var fullFirst = fullSegments[0].DataSetReference;
        var fullSecond = fullSegments[1].DataSetReference;
        var isolated = Assert.Single(isolatedSecond.AcquisitionPlan.Segments).DataSetReference;

        Assert.NotEqual(fullFirst, fullSecond);
        Assert.Equal(fullSecond, isolated);
        Assert.StartsWith("LD0/LLN0.AR_HYB_", fullSecond, StringComparison.OrdinalIgnoreCase);
        Assert.False(fullSecond.EndsWith("_01", StringComparison.OrdinalIgnoreCase));
        Assert.False(fullSecond.EndsWith("_02", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StableIdentity_ChangesOnlyDynamicDataSetReference_NotRcbOrMembersOrProductionBoundary()
    {
        var source = CapabilityPlan(
            DynamicSegment("LD0/LLN0.RP.U01", "LD0/LLN0.AR_HYB_01", "LD0/GGIO1$ST$Ind1$stVal"));
        var result = MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner.WithStableDynamicDataSetIdentities(source);

        var before = Assert.Single(source.AcquisitionPlan.Segments);
        var after = Assert.Single(result.AcquisitionPlan.Segments);

        Assert.Equal(before.ReportControlReference, after.ReportControlReference);
        Assert.Equal(before.ReportPlan!.DynamicPoints.Select(point => point.MmsReference),
            after.ReportPlan!.DynamicPoints.Select(point => point.MmsReference));
        Assert.NotEqual(before.DataSetReference, after.DataSetReference);
        Assert.False(result.ProductionDynamicActivationAuthorized);
        Assert.Equal(source.ProductionDynamicAuthorizationReason, result.ProductionDynamicAuthorizationReason);
    }

    [Fact]
    public void StaticSegment_IsReturnedUnchanged()
    {
        var staticSegment = new MmsHybridAcquisitionSegment
        {
            Kind = MmsHybridAcquisitionKind.StaticUrcb,
            Activation = MmsHybridReportActivation.EnableExistingDataSet,
            ReportPlan = new MmsReportSubscriptionPlan
            {
                Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
                Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
                ReportControl = Candidate("LD0/LLN0.RP.Static01"),
                DataSetReference = "LD0/LLN0.StaticData"
            }
        };
        var source = CapabilityPlan(staticSegment);
        var result = MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner.WithStableDynamicDataSetIdentities(source);

        Assert.Same(source, result);
    }

    private static MmsCapabilityAwareHybridReportAcquisitionPlan CapabilityPlan(params MmsHybridAcquisitionSegment[] segments)
        => new()
        {
            AcquisitionPlan = new MmsHybridReportAcquisitionPlan
            {
                Status = MmsHybridAcquisitionPlanStatus.FullReportCoverage,
                Segments = segments,
                Assignments = segments.SelectMany(segment => segment.Signals.Select(signal => new MmsHybridSignalAssignment
                {
                    SignalReference = signal.EffectiveMmsReference,
                    Kind = segment.Kind,
                    ReportControlReference = segment.ReportControlReference,
                    DataSetReference = segment.DataSetReference,
                    IsReportBacked = true,
                    Reason = "fixture"
                })).ToArray()
            },
            ProductionDynamicActivationAuthorized = false,
            ProductionDynamicAuthorizationReason = "ProductionEligible remains separate."
        };

    private static MmsHybridAcquisitionSegment DynamicSegment(string rcb, string dataSet, string member)
    {
        var point = Point(member);
        var signal = new AR.Iec61850.Discovery.Iec61850SignalDescriptor
        {
            EffectiveMmsReference = member,
            CanonicalMmsReference = member,
            FunctionalConstraint = "ST",
            SemanticRole = AR.Iec61850.Discovery.Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsOperationalCandidate = true
        };
        return new MmsHybridAcquisitionSegment
        {
            Kind = MmsHybridAcquisitionKind.DynamicUrcb,
            Activation = MmsHybridReportActivation.ConfigureDynamicDataSet,
            Signals = [signal],
            RequiresWrite = true,
            ReportPlan = new MmsReportSubscriptionPlan
            {
                Mode = MmsReportSubscriptionPlanMode.DynamicDataSet,
                Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
                ReportControl = Candidate(rcb),
                DataSetReference = dataSet,
                DynamicPoints = [point],
                Members = [new MmsDataSetDirectoryMember { Domain = point.Domain, MmsItemName = point.MmsItemName }],
                Steps = [$"Create dynamic DataSet {dataSet}."]
            }
        };
    }

    private static MmsReportControlCandidate Candidate(string reference)
        => new()
        {
            Domain = reference.Split('/')[0],
            LogicalNode = "LLN0",
            FunctionalConstraint = "RP",
            Name = reference.Split('.').Last(),
            Reference = reference,
            Buffered = false,
            EnabledState = "false",
            ReservationState = "false"
        };

    private static MmsFcResolvedPoint Point(string member)
    {
        var slash = member.IndexOf('/');
        var domain = member[..slash];
        var item = member[(slash + 1)..];
        return new MmsFcResolvedPoint
        {
            Domain = domain,
            LogicalNode = "GGIO1",
            FunctionalConstraint = "ST",
            DataObjectPath = "Ind.stVal",
            MmsItemName = item,
            Source = "fixture",
            Confidence = 100
        };
    }
}
