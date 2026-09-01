using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsSemanticReportValueProjectorTests
{
    [Fact]
    public void Whole_MultiPhase_Member_Fans_Out_All_Scalar_Leaves_Without_Selecting_Primary_Phase()
    {
        const string objectReference = "AA1E1F02R2VI3p1_THDHarmonics/I_MHAI1.ThdA";
        const string dataSetReference = "AA1E1F02R2Application/LLN0.Analog";
        var model = BuildThreePhaseModel(objectReference, dataSetReference);
        var binding = Assert.Single(Iec61850DataSetSemanticBindingResolver.Resolve(model).Members);

        Assert.Equal(LiveIedDataSetMemberResolutionStatus.Ambiguous, binding.ResolutionStatus);
        Assert.Null(binding.PrimaryValue);

        // Physical bench evidence included phsB.cVal.mag.f = 40.04636.
        // Keep that exact value here so the report fan-out regression stays tied to the
        // field failure that exposed REPORT_RAW_STRUCT for the whole ThdA member.
        var frame = BuildFrame(
            objectReference,
            dataSetReference,
            PhaseValue(19.97612),
            PhaseValue(40.04636),
            PhaseValue(60.02344));

        var projection = MmsSemanticReportValueProjector.Project(
            frame,
            MmsReportSemanticProjectionContext.Create(model));

        var phaseA = Assert.Single(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsA.cVal.mag.f", StringComparison.OrdinalIgnoreCase));
        var phaseB = Assert.Single(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsB.cVal.mag.f", StringComparison.OrdinalIgnoreCase));
        var phaseC = Assert.Single(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsC.cVal.mag.f", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("19.97612", phaseA.Value);
        Assert.Equal("40.04636", phaseB.Value);
        Assert.Equal("60.02344", phaseC.Value);
        Assert.All(new[] { phaseA, phaseB, phaseC }, update =>
        {
            Assert.True(update.IsProjectedChild);
            Assert.Equal("semantic-structured-leaf", update.ProjectionStatus);
        });
        Assert.DoesNotContain(projection.Updates, update =>
            update.Reference.Equals(objectReference, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projection.Warnings, warning =>
            warning.StartsWith("REPORT_RAW_STRUCT:", StringComparison.OrdinalIgnoreCase)
            && warning.Contains(objectReference, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Warnings, warning =>
            warning.StartsWith("REPORT_SEMANTIC_STRUCT:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Schema_Mismatch_Fails_Closed_And_Preserves_Raw_Projection()
    {
        const string objectReference = "AA1E1F02R2VI3p1_THDHarmonics/I_MHAI1.ThdA";
        const string dataSetReference = "AA1E1F02R2Application/LLN0.Analog";
        var model = BuildThreePhaseModel(objectReference, dataSetReference);
        var frame = BuildFrame(
            objectReference,
            dataSetReference,
            PhaseValue(19.97612),
            PhaseValue(40.04636));

        var projection = MmsSemanticReportValueProjector.Project(
            frame,
            MmsReportSemanticProjectionContext.Create(model));

        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals(objectReference, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Warnings, warning =>
            warning.StartsWith("REPORT_RAW_STRUCT:", StringComparison.OrdinalIgnoreCase)
            && warning.Contains(objectReference, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Warnings, warning =>
            warning.StartsWith("REPORT_SEMANTIC_FALLBACK:", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("child-count mismatch", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projection.Updates, update =>
            update.ProjectionStatus.Equals("semantic-structured-leaf", StringComparison.OrdinalIgnoreCase));
    }

    private static LiveIedModelDiscoveryDocument BuildThreePhaseModel(
        string objectReference,
        string dataSetReference)
    {
        var attributes = new[]
        {
            Attribute(objectReference + ".phsA.cVal.mag.f", "phsA.cVal.mag.f"),
            Attribute(objectReference + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"),
            Attribute(objectReference + ".phsC.cVal.mag.f", "phsC.cVal.mag.f")
        };
        var slash = objectReference.IndexOf('/');
        var domain = objectReference[..slash];
        var logicalPath = objectReference[(slash + 1)..];
        var firstDot = logicalPath.IndexOf('.');
        var logicalNode = logicalPath[..firstDot];
        var dataObjectName = logicalPath[(firstDot + 1)..];

        return new LiveIedModelDiscoveryDocument
        {
            Source = "SclWorkspace",
            IedName = "AA1E1F02R2",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain,
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = logicalNode,
                            LnClass = "MHAI",
                            LnInst = "1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = objectReference,
                                    Name = dataObjectName,
                                    InferredCdc = "WYE",
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
                    Reference = dataSetReference,
                    Domain = "AA1E1F02R2Application",
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = 1,
                    Members = new[]
                    {
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = objectReference,
                            FunctionalConstraint = "MX",
                            MmsReference = "AA1E1F02R2VI3p1_THDHarmonics/I_MHAI1$MX$ThdA",
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }
                    }
                }
            }
        };
    }

    private static LiveIedDataAttributeModel Attribute(string reference, string path)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "MX",
            MmsReference = reference.Replace('.', '$'),
            SclBType = "FLOAT32",
            MmsType = "floating-point",
            Source = "SCL.DataTypeTemplates",
            TypeSource = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static MmsReportFrame BuildFrame(
        string memberReference,
        string dataSetReference,
        params MmsDataValue[] phases)
        => new()
        {
            ReceivedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            Header = new MmsReportHeader { DataSetReference = dataSetReference },
            Values = new[]
            {
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = memberReference,
                        FunctionalConstraint = "MX"
                    },
                    Value = MmsDataValue.Structure(phases),
                    ReasonForInclusion = new[] { "data-change" }
                }
            }
        };

    private static MmsDataValue PhaseValue(double value)
        => MmsDataValue.Structure(new[]
        {
            MmsDataValue.Structure(new[]
            {
                MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(value) })
            })
        });
}
