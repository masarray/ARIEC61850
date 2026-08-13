using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850StandardEnumRegistryTests
{
    [Fact]
    public void Behaviour_Uses_Standard_On_Test_And_Off_Ordinals()
    {
        var definition = Iec61850StandardEnumRegistry.Resolve("LLN0", "Beh", "INS", "stVal");

        Assert.Equal("on", definition.Values.Single(value => value.Ord == 1).Symbol);
        Assert.Equal("test", definition.Values.Single(value => value.Ord == 3).Symbol);
        Assert.Equal("off", definition.Values.Single(value => value.Ord == 5).Symbol);
    }
}

public sealed class Iec61850DataSetSemanticBindingRegressionTests
{
    [Fact]
    public void CcppStyleBcrFcd_PreservesMembersAndResolvesActVal()
    {
        var document = BuildDocument(BuildBcr("SupWh"), BuildBcr("DmdWh"));

        var dataSet = Assert.Single(Iec61850DataSetSemanticBindingResolver.Resolve(document).DataSets);

        Assert.Collection(
            dataSet.Members,
            first => AssertBinding(first, 17, "IED/LD/MMTR1.SupWh [ST]", "IEDLD/MMTR1.SupWh", "IEDLD/MMTR1$ST$SupWh$actVal"),
            second => AssertBinding(second, 18, "IED/LD/MMTR1.DmdWh [ST]", "IEDLD/MMTR1.DmdWh", "IEDLD/MMTR1$ST$DmdWh$actVal"));
    }

    [Fact]
    public void ShallowMmsBcr_UsesActValAndNeverStVal()
    {
        var document = BuildDocument(new LiveIedDataObjectModel
        {
            Reference = "IEDLD/MMTR1$ST$SupWh",
            Name = "SupWh",
            InferredCdc = "BCR"
        });
        document = WithMembers(document, new LiveIedDataSetMemberModel
        {
            Index = 1,
            Reference = "IEDLD/MMTR1$ST$SupWh",
            FunctionalConstraint = "ST"
        });

        var binding = Assert.Single(Iec61850DataSetSemanticBindingResolver.Resolve(document).Members);

        Assert.Equal(LiveIedDataSetMemberResolutionStatus.CdcFallback, binding.ResolutionStatus);
        Assert.Equal("IEDLD/MMTR1.SupWh.actVal", binding.PrimaryValueReference);
        Assert.Equal("IEDLD/MMTR1$ST$SupWh$actVal", binding.PrimaryValueMmsReference);
        Assert.DoesNotContain(binding.ResolvedAttributes, attribute => attribute.Reference.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TypedEvidenceWithWrongFc_DoesNotUseCdcFallback()
    {
        var document = BuildDocument(BuildBcr("SupWh", "MX"));

        var binding = Assert.Single(Iec61850DataSetSemanticBindingResolver.Resolve(document).Members);

        Assert.Equal(LiveIedDataSetMemberResolutionStatus.FunctionalConstraintMismatch, binding.ResolutionStatus);
        Assert.Empty(binding.ResolvedAttributes);
    }

    private static LiveIedModelDiscoveryDocument BuildDocument(params LiveIedDataObjectModel[] dataObjects)
        => new()
        {
            IedName = "IED",
            Source = "SclOfflineProjection",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD",
                    Inst = "LD",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMTR1",
                            LnClass = "MMTR",
                            LnInst = "1",
                            DataObjects = dataObjects
                        }
                    }
                }
            },
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = "IEDLD/LLN0$DS$Energy",
                    Members = dataObjects.Select((dataObject, offset) => new LiveIedDataSetMemberModel
                    {
                        Index = 17 + offset,
                        Reference = $"IED/LD/MMTR1.{dataObject.Name} [ST]",
                        FunctionalConstraint = "ST"
                    }).ToArray()
                }
            }
        };

    private static LiveIedModelDiscoveryDocument WithMembers(LiveIedModelDiscoveryDocument source, params LiveIedDataSetMemberModel[] members)
        => new()
        {
            IedName = source.IedName,
            Source = source.Source,
            LogicalDevices = source.LogicalDevices,
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = "IEDLD/LLN0$DS$Energy",
                    Members = members
                }
            }
        };

    private static LiveIedDataObjectModel BuildBcr(string name, string fc = "ST")
    {
        var reference = $"IEDLD/MMTR1.{name}";
        return new LiveIedDataObjectModel
        {
            Reference = reference,
            Name = name,
            InferredCdc = "BCR",
            Attributes = new[]
            {
                Attribute(reference, "actVal", fc, "INT64"),
                Attribute(reference, "frVal", fc, "INT64"),
                Attribute(reference, "q", fc, "Quality"),
                Attribute(reference, "t", fc, "Timestamp")
            }
        };
    }

    private static LiveIedDataAttributeModel Attribute(string objectReference, string path, string fc, string bType)
        => new()
        {
            ObjectReference = $"{objectReference}.{path}",
            AttributePath = path,
            FunctionalConstraint = fc,
            SclBType = bType,
            Source = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static void AssertBinding(LiveIedDataSetMemberSemanticBinding binding, int index, string original, string canonical, string mms)
    {
        Assert.Equal(index, binding.Index);
        Assert.Equal(original, binding.OriginalReference);
        Assert.Equal(canonical, binding.CanonicalReference);
        Assert.Equal(LiveIedDataSetMemberResolutionStatus.TemplateResolved, binding.ResolutionStatus);
        Assert.Equal($"{canonical}.actVal", binding.PrimaryValueReference);
        Assert.Equal(mms, binding.PrimaryValueMmsReference);
        Assert.Contains(binding.ResolvedAttributes, attribute => attribute.SemanticRole == Iec61850DataAttributeSemanticRole.FrozenValue);
        Assert.Contains(binding.ResolvedAttributes, attribute => attribute.SemanticRole == Iec61850DataAttributeSemanticRole.Quality);
        Assert.Contains(binding.ResolvedAttributes, attribute => attribute.SemanticRole == Iec61850DataAttributeSemanticRole.Timestamp);
        Assert.DoesNotContain(binding.ResolvedAttributes, attribute => attribute.Reference.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase));
    }
}
