using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850DataSetSignalInventoryProjectionTests
{
    [Fact]
    public void FcdOnly_DataSets_Preserve_All_58_Members_For_Application_Selection()
    {
        var digitalMembers = Enumerable.Range(1, 36)
            .Select(index => Member(index - 1, $"IEDLD0/GGIO1.Dig{index:00}", "ST"))
            .ToArray();
        var analogMembers = Enumerable.Range(1, 22)
            .Select(index => Member(index - 1, $"IEDLD0/MMXU1.Ana{index:00}", "MX"))
            .ToArray();
        var digitalObjects = digitalMembers.Select(member => DataObject(member.Reference, "SPS")).ToArray();
        var analogObjects = analogMembers.Select(member => DataObject(member.Reference, "MV")).ToArray();
        var design = new LiveIedModelDiscoveryDocument
        {
            Source = "LiveMmsDiscovery",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel { Name = "GGIO1", LnClass = "GGIO", LnInst = "1", DataObjects = digitalObjects },
                        new LiveIedLogicalNodeModel { Name = "MMXU1", LnClass = "MMXU", LnInst = "1", DataObjects = analogObjects }
                    }
                }
            },
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = "IEDLD0/LLN0.Digital",
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Digital",
                    MemberCount = digitalMembers.Length,
                    Members = digitalMembers
                },
                new LiveIedDataSetModel
                {
                    Reference = "IEDLD0/LLN0.Analog",
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = analogMembers.Length,
                    Members = analogMembers
                }
            }
        };

        var signals = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(design);

        Assert.Equal(58, signals.Count);
        Assert.Equal(58, signals
            .SelectMany(signal => signal.DataSetMemberships)
            .Select(membership => $"{membership.DataSetReference}[{membership.MemberIndex}]")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
        Assert.All(signals, signal =>
        {
            Assert.True(signal.IsStaticDataSetMandatory);
            Assert.False(signal.IsEngineeringOnly);
            Assert.Equal(Iec61850SignalCatalogResolutionStatus.Unresolved, signal.ResolutionStatus);
            Assert.Single(signal.DataSetMemberships);
            Assert.False(string.IsNullOrWhiteSpace(signal.DesignReference));
        });
    }

    [Fact]
    public void Resolved_Primary_Value_Keeps_Static_Fcd_Identity_Separate_From_Runtime_Leaf()
    {
        const string objectReference = "IEDLD0/GGIO1.Ind1";
        const string valueReference = objectReference + ".stVal";
        var design = BuildSingleMemberDesign(
            objectReference,
            "SPS",
            "ST",
            new LiveIedDataAttributeModel
            {
                ObjectReference = valueReference,
                AttributePath = "stVal",
                FunctionalConstraint = "ST",
                MmsReference = "IEDLD0/GGIO1$ST$Ind1$stVal",
                MmsItemName = "GGIO1$ST$Ind1$stVal",
                SclBType = "BOOLEAN",
                MmsType = "boolean",
                Source = "SCL.DataTypeTemplates"
            });

        var signal = Assert.Single(Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(design));
        var membership = Assert.Single(signal.DataSetMemberships);

        Assert.Equal(valueReference, signal.DesignReference);
        Assert.Equal(valueReference, signal.PrimaryValueReference);
        Assert.Equal(objectReference, membership.OriginalMemberReference);
        Assert.Equal(objectReference, membership.CanonicalMemberReference);
        Assert.DoesNotContain(".stVal", membership.CanonicalMemberReference, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Iec61850DataAttributeSemanticRole.PrimaryValue, signal.SemanticRole);
        Assert.NotEqual(Iec61850SignalCatalogResolutionStatus.Unresolved, signal.ResolutionStatus);
    }

    [Fact]
    public void Multiple_Primary_Candidates_Still_Produce_Exactly_One_Static_Member_Descriptor()
    {
        const string objectReference = "IEDLD0/MMXU1.A";
        var design = BuildSingleMemberDesign(
            objectReference,
            "MV",
            "MX",
            new LiveIedDataAttributeModel
            {
                ObjectReference = objectReference + ".mag.f",
                AttributePath = "mag.f",
                FunctionalConstraint = "MX",
                SclBType = "FLOAT32",
                Source = "SCL.DataTypeTemplates"
            },
            new LiveIedDataAttributeModel
            {
                ObjectReference = objectReference + ".ang.f",
                AttributePath = "ang.f",
                FunctionalConstraint = "MX",
                SclBType = "FLOAT32",
                Source = "SCL.DataTypeTemplates"
            });

        var signals = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(design);

        var signal = Assert.Single(signals);
        var membership = Assert.Single(signal.DataSetMemberships);
        Assert.Equal(objectReference, membership.CanonicalMemberReference);
        Assert.Equal(Iec61850SignalCatalogResolutionStatus.Unresolved, signal.ResolutionStatus);
        Assert.Equal(objectReference, signal.DesignReference);
        Assert.Contains(signal.Evidence, evidence =>
            evidence.Message.Contains("without guessing a runtime leaf", StringComparison.OrdinalIgnoreCase));
    }

    private static LiveIedModelDiscoveryDocument BuildSingleMemberDesign(
        string objectReference,
        string cdc,
        string fc,
        params LiveIedDataAttributeModel[] attributes)
    {
        var slash = objectReference.IndexOf('/');
        var logicalPath = objectReference[(slash + 1)..];
        var dot = logicalPath.IndexOf('.');
        var logicalNode = logicalPath[..dot];
        var lnClass = new string(logicalNode.TakeWhile(char.IsLetter).ToArray());
        var dataObjectName = logicalPath[(dot + 1)..];
        return new LiveIedModelDiscoveryDocument
        {
            Source = "SclProjection",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = objectReference[..slash],
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = logicalNode,
                            LnClass = lnClass,
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
                    Reference = "IEDLD0/LLN0.Events",
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Events",
                    MemberCount = 1,
                    Members = new[] { Member(0, objectReference, fc) }
                }
            }
        };
    }

    private static LiveIedDataSetMemberModel Member(int index, string reference, string functionalConstraint)
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

    private static LiveIedDataObjectModel DataObject(string reference, string cdc)
        => new()
        {
            Reference = reference,
            Name = reference[(reference.LastIndexOf('.') + 1)..],
            InferredCdc = cdc,
            ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Low,
            Attributes = Array.Empty<LiveIedDataAttributeModel>()
        };
}
