using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850StructuredDataSetMemberBindingTests
{
    [Fact]
    public void Phase_Member_Resolves_Canonical_Magnitude_Without_Sibling_Phase()
    {
        const string objectReference = "IEDLD0/MMXU1.A";
        const string memberReference = objectReference + ".phsA";
        const string expected = memberReference + ".cVal.mag.f";
        var design = BuildDesign(
            objectReference,
            "WYE",
            memberReference,
            Attribute(expected, "phsA.cVal.mag.f"),
            Attribute(memberReference + ".instCVal.mag.f", "phsA.instCVal.mag.f"),
            Attribute(memberReference + ".cVal.ang.f", "phsA.cVal.ang.f"),
            Attribute(memberReference + ".q", "phsA.q", "Quality"),
            Attribute(memberReference + ".t", "phsA.t", "Timestamp"),
            Attribute(objectReference + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"));

        var binding = Assert.Single(Iec61850DataSetSemanticBindingResolver.Resolve(design).Members);

        Assert.Equal(LiveIedDataSetMemberResolutionStatus.TemplateResolved, binding.ResolutionStatus);
        Assert.Equal(expected, binding.PrimaryValueReference);
        Assert.DoesNotContain(binding.ResolvedAttributes, attribute =>
            attribute.Reference.Contains(".phsB.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(binding.ResolvedAttributes, attribute =>
            attribute.Reference.EndsWith(".phsA.cVal.ang.f", StringComparison.OrdinalIgnoreCase));

        var signal = Assert.Single(Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(design));
        var membership = Assert.Single(signal.DataSetMemberships);
        Assert.Equal(memberReference, membership.CanonicalMemberReference);
        Assert.Equal(expected, signal.PrimaryValueReference);
        Assert.Equal(expected, signal.DesignReference);
        Assert.True(membership.IsPrimaryValueForMember);
        Assert.NotEqual(Iec61850SignalCatalogResolutionStatus.Unresolved, signal.ResolutionStatus);
    }

    [Fact]
    public void PhasePair_Member_Resolves_Only_That_Pair_Magnitude()
    {
        const string objectReference = "IEDLD0/MMXU1.PPV";
        const string memberReference = objectReference + ".phsAB";
        const string expected = memberReference + ".cVal.mag.f";
        var design = BuildDesign(
            objectReference,
            "DEL",
            memberReference,
            Attribute(expected, "phsAB.cVal.mag.f"),
            Attribute(memberReference + ".cVal.ang.f", "phsAB.cVal.ang.f"),
            Attribute(objectReference + ".phsBC.cVal.mag.f", "phsBC.cVal.mag.f"));

        var signal = Assert.Single(Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(design));

        Assert.Equal(expected, signal.PrimaryValueReference);
        Assert.Equal(memberReference, Assert.Single(signal.DataSetMemberships).CanonicalMemberReference);
    }

    [Fact]
    public void Whole_Structured_Member_With_Multiple_Phase_Magnitudes_Remains_Ambiguous()
    {
        const string objectReference = "IEDLD0/MMXU1.A";
        var design = BuildDesign(
            objectReference,
            "WYE",
            objectReference,
            Attribute(objectReference + ".phsA.cVal.mag.f", "phsA.cVal.mag.f"),
            Attribute(objectReference + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"),
            Attribute(objectReference + ".phsC.cVal.mag.f", "phsC.cVal.mag.f"));

        var binding = Assert.Single(Iec61850DataSetSemanticBindingResolver.Resolve(design).Members);
        Assert.Equal(LiveIedDataSetMemberResolutionStatus.Ambiguous, binding.ResolutionStatus);
        Assert.Null(binding.PrimaryValue);

        var signal = Assert.Single(Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(design));
        Assert.Equal(Iec61850SignalCatalogResolutionStatus.Unresolved, signal.ResolutionStatus);
        Assert.Equal(objectReference, signal.DesignReference);
    }

    private static LiveIedModelDiscoveryDocument BuildDesign(
        string objectReference,
        string cdc,
        string memberReference,
        params LiveIedDataAttributeModel[] attributes)
    {
        var slash = objectReference.IndexOf('/');
        var domain = objectReference[..slash];
        var logicalPath = objectReference[(slash + 1)..];
        var firstDot = logicalPath.IndexOf('.');
        var logicalNode = logicalPath[..firstDot];
        var dataObjectName = logicalPath[(firstDot + 1)..];

        return new LiveIedModelDiscoveryDocument
        {
            Source = "SclWorkspace",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain,
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = logicalNode,
                            LnClass = "MMXU",
                            LnInst = "1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = objectReference,
                                    Name = dataObjectName,
                                    InferredCdc = cdc,
                                    Attributes = attributes
                                }
                            }
                        }
                    }
                }
            },
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = domain + "/LLN0.Analog",
                    Domain = domain,
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = 1,
                    Members = new[] { Member(0, memberReference, "MX") }
                }
            }
        };
    }

    private static LiveIedDataAttributeModel Attribute(
        string reference,
        string attributePath,
        string sclBType = "FLOAT32")
    {
        var slash = reference.IndexOf('/');
        var domain = reference[..slash];
        var logicalPath = reference[(slash + 1)..];
        var firstDot = logicalPath.IndexOf('.');
        var logicalNode = logicalPath[..firstDot];
        var objectAndAttribute = logicalPath[(firstDot + 1)..].Replace('.', '$');
        var mmsItem = $"{logicalNode}$MX${objectAndAttribute}";
        return new LiveIedDataAttributeModel
        {
            ObjectReference = reference,
            AttributePath = attributePath,
            FunctionalConstraint = "MX",
            MmsReference = $"{domain}/{mmsItem}",
            MmsItemName = mmsItem,
            SclBType = sclBType,
            MmsType = sclBType == "FLOAT32" ? "floating-point" : string.Empty,
            Source = "SCL.DataTypeTemplates",
            TypeSource = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
    }

    private static LiveIedDataSetMemberModel Member(
        int index,
        string reference,
        string functionalConstraint)
    {
        var slash = reference.IndexOf('/');
        var domain = reference[..slash];
        var path = reference[(slash + 1)..];
        var firstDot = path.IndexOf('.');
        var logicalNode = path[..firstDot];
        var objectPath = path[(firstDot + 1)..].Replace('.', '$');
        return new LiveIedDataSetMemberModel
        {
            Index = index,
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            MmsReference = $"{domain}/{logicalNode}${functionalConstraint}${objectPath}",
            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
    }
}
