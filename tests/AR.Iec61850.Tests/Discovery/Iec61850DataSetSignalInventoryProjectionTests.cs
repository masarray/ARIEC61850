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
        var digitalObjects = digitalMembers
            .Select(member => DataObject(member.Reference, "SPS"))
            .ToArray();
        var analogObjects = analogMembers
            .Select(member => DataObject(member.Reference, "MV"))
            .ToArray();
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
                        new LiveIedLogicalNodeModel
                        {
                            Name = "GGIO1",
                            LnClass = "GGIO",
                            LnInst = "1",
                            DataObjects = digitalObjects
                        },
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMXU1",
                            LnClass = "MMXU",
                            LnInst = "1",
                            DataObjects = analogObjects
                        }
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
            Assert.Contains(signal.Evidence, evidence =>
                evidence.Kind == Iec61850SignalEvidenceKind.DataSetSemanticBinding);
        });
        Assert.Contains(signals, signal => signal.DesignReference == "IEDLD0/GGIO1.Dig01");
        Assert.Contains(signals, signal => signal.DesignReference == "IEDLD0/MMXU1.Ana22");
    }

    [Fact]
    public void Resolved_Primary_Value_Wins_Without_Adding_Fcd_Placeholder()
    {
        const string objectReference = "IEDLD0/GGIO1.Ind1";
        const string valueReference = objectReference + ".stVal";
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
                        new LiveIedLogicalNodeModel
                        {
                            Name = "GGIO1",
                            LnClass = "GGIO",
                            LnInst = "1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = objectReference,
                                    Name = "Ind1",
                                    InferredCdc = "SPS",
                                    Attributes = new[]
                                    {
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = valueReference,
                                            AttributePath = "stVal",
                                            FunctionalConstraint = "ST",
                                            MmsReference = "IEDLD0/GGIO1$ST$Ind1$stVal",
                                            MmsItemName = "GGIO1$ST$Ind1$stVal",
                                            SclBType = "BOOLEAN",
                                            MmsType = "boolean",
                                            Source = "LiveMmsDiscovery"
                                        }
                                    }
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
                    Members = new[] { Member(0, objectReference, "ST") }
                }
            }
        };

        var signals = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(design);

        var signal = Assert.Single(signals);
        Assert.Equal(valueReference, signal.DesignReference);
        Assert.Equal(Iec61850DataAttributeSemanticRole.PrimaryValue, signal.SemanticRole);
        Assert.NotEqual(Iec61850SignalCatalogResolutionStatus.Unresolved, signal.ResolutionStatus);
        Assert.Equal(0, Assert.Single(signal.DataSetMemberships).MemberIndex);
    }

    private static LiveIedDataSetMemberModel Member(int index, string reference, string functionalConstraint)
    {
        var slash = reference.IndexOf('/');
        var domain = reference[..slash];
        var path = reference[(slash + 1)..];
        var firstDot = path.IndexOf('.');
        var logicalNode = path[..firstDot];
        var objectPath = path[(firstDot + 1)..].Replace('.', '$');
        var mmsReference = $"{domain}/{logicalNode}${functionalConstraint}${objectPath}";
        return new LiveIedDataSetMemberModel
        {
            Index = index,
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            MmsReference = mmsReference,
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
