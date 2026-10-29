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

        Assert.Equal("19.976", phaseA.Value);
        Assert.Equal("40.046", phaseB.Value);
        Assert.Equal("60.023", phaseC.Value);
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
    public void Sparse_Report_Value_Index_Drift_Still_Uses_Exact_Static_Member_Reference()
    {
        const string objectReference = "AA1E1F02R2VI3p1_THDHarmonics/I_MHAI1.ThdA";
        const string dataSetReference = "AA1E1F02R2Application/LLN0.Analog";
        var model = BuildThreePhaseModel(objectReference, dataSetReference);

        var frame = BuildFrame(
            objectReference,
            dataSetReference,
            reportValueIndex: 17,
            PhaseValue(19.97612),
            PhaseValue(40.04636),
            PhaseValue(60.02344));

        var projection = MmsSemanticReportValueProjector.Project(
            frame,
            MmsReportSemanticProjectionContext.Create(model));

        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsA.cVal.mag.f", StringComparison.OrdinalIgnoreCase)
            && update.ProjectionStatus.Equals("semantic-structured-leaf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsB.cVal.mag.f", StringComparison.OrdinalIgnoreCase)
            && update.Value == "40.046");
        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsC.cVal.mag.f", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projection.Warnings, warning =>
            warning.StartsWith("REPORT_SEMANTIC_FALLBACK:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Exact_Static_Schema_Overrides_Generic_InstMagMag_Heuristic()
    {
        const string objectReference = "AA1E1F06R4VI3p1_OperationalValues/PPRE_MMXU1.TotPF";
        const string dataSetReference = "AA1E1F06R4Application/LLN0.Analog";
        var timestamp = new DateTimeOffset(2026, 9, 4, 13, 20, 51, TimeSpan.Zero);
        var model = BuildMeasurementPairModel(objectReference, dataSetReference);
        var frame = BuildFrame(
            objectReference,
            dataSetReference,
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(0.125) }),
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(0.25) }),
            MmsDataValue.BitString(3, new byte[] { 0x00, 0x00 }),
            MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0)));

        var baseline = MmsReportValueProjector.Project(frame);
        Assert.Contains(baseline.Updates, update =>
            update.ProjectionStatus.Equals("projected-mx-pair", StringComparison.OrdinalIgnoreCase));

        var projection = MmsSemanticReportValueProjector.Project(
            frame,
            MmsReportSemanticProjectionContext.Create(model));

        var instant = Assert.Single(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".instMag.f", StringComparison.OrdinalIgnoreCase));
        var magnitude = Assert.Single(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".mag.f", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("semantic-structured-leaf", instant.ProjectionStatus);
        Assert.Equal("semantic-structured-leaf", magnitude.ProjectionStatus);
        Assert.Equal("good", magnitude.Quality);
        Assert.True(magnitude.HasQuality);
        Assert.True(magnitude.HasTimestamp);
        Assert.Contains("2026-09-04", magnitude.Timestamp, StringComparison.Ordinal);
        Assert.DoesNotContain(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".instMag", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".mag", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Warnings, warning =>
            warning.StartsWith("REPORT_SEMANTIC_STRUCT:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_MemberReference_Uses_Static_DataSet_Index_Without_Leaking_Generic_Heuristic()
    {
        const string objectReference = "AA1E1F06R4VI3p1_OperationalValues/PPRE_MMXU1.TotPF";
        const string dataSetReference = "AA1E1F06R4Application/LLN0.Analog";
        var timestamp = new DateTimeOffset(2026, 9, 4, 13, 20, 51, TimeSpan.Zero);
        var model = BuildMeasurementPairModel(objectReference, dataSetReference);
        var frame = BuildFrame(
            string.Empty,
            dataSetReference,
            reportValueIndex: 0,
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(0.125) }),
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(0.25) }),
            MmsDataValue.BitString(3, new byte[] { 0x00, 0x00 }),
            MmsDataValue.UtcTime(new Iec61850UtcTime(timestamp, 0)));

        var baseline = MmsReportValueProjector.Project(frame);
        Assert.Contains(baseline.Updates, update =>
            update.ProjectionStatus.Equals("projected-mx-pair", StringComparison.OrdinalIgnoreCase));

        var projection = MmsSemanticReportValueProjector.Project(
            frame,
            MmsReportSemanticProjectionContext.Create(model));

        var instant = Assert.Single(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".instMag.f", StringComparison.OrdinalIgnoreCase));
        var magnitude = Assert.Single(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".mag.f", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("semantic-structured-leaf", instant.ProjectionStatus);
        Assert.Equal("semantic-structured-leaf", magnitude.ProjectionStatus);
        Assert.DoesNotContain(projection.Updates, update =>
            update.ProjectionStatus.Equals("projected-mx-pair", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projection.Updates, update =>
            update.Reference.Equals("instMag.f", StringComparison.OrdinalIgnoreCase)
            || update.Reference.Equals("mag.f", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Warnings, warning =>
            warning.StartsWith("REPORT_SEMANTIC_STRUCT:", StringComparison.OrdinalIgnoreCase)
            && warning.Contains(objectReference, StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void C264_Incomplete_Live_Schema_Still_Projects_Strictly_Typed_Mod_FCD()
    {
        // C264 smart discovery can expose stVal without type-probing every
        // sibling. That partial model must not suppress a fully typed ST
        // Report containing stVal, q, t and stSeld.
        const string root = "C264CONTROL/CILO1.Mod";
        const string dataSet = "C264CONTROL/LLN0.LLN0BRptStDs";
        var schema = new LiveIedModelDiscoveryDocument
        {
            IedName = "C264",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "C264CONTROL",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "CILO1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = root,
                                    Name = "Mod",
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = root + ".stVal",
                                            AttributePath = "stVal",
                                            FunctionalConstraint = "ST"
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ],
            DataSets =
            [
                new LiveIedDataSetModel
                {
                    Reference = dataSet,
                    Domain = "C264CONTROL",
                    LogicalNode = "LLN0",
                    Name = "LLN0BRptStDs",
                    MemberCount = 1,
                    Members =
                    [
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = root,
                            FunctionalConstraint = "ST",
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }
                    ]
                }
            ]
        };
        var at = new DateTimeOffset(2026, 10, 17, 2, 24, 5, TimeSpan.Zero);
        var frame = new MmsReportFrame
        {
            ReceivedAt = at,
            Header = new MmsReportHeader { DataSetReference = dataSet },
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = root,
                        FunctionalConstraint = "ST"
                    },
                    Value = MmsDataValue.Structure([
                        MmsDataValue.Integer(1),
                        MmsDataValue.BitString(3, [0x00, 0x00]),
                        MmsDataValue.UtcTime(new Iec61850UtcTime(at, 0)),
                        MmsDataValue.Boolean(false)
                    ])
                }
            ]
        };

        var projected = MmsSemanticReportValueProjector.Project(
            frame, MmsReportSemanticProjectionContext.Create(schema));
        var scalar = Assert.Single(projected.Updates);
        Assert.Equal(root + ".stVal", scalar.Reference);
        Assert.Equal("good", scalar.Quality);
        Assert.True(scalar.HasTimestamp);
        Assert.DoesNotContain(projected.Warnings,
            x => x.StartsWith("REPORT_RAW_STRUCT:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projected.Updates,
            x => x.Value.StartsWith("Structure(", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExactTypedFcd_SwappedQualityAndTimeIsRejectedWithoutFalseValuePublication()
    {
        const string root = "C264MEAS/MMXU1.A";
        const string dataSet = "C264MASTER/LLN0.Analog";
        var model = BuildMeasurementPairModel(root, dataSet);
        var when = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
        var invalid = BuildFrame(root, dataSet,
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(12f) }),
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(13f) }),
            MmsDataValue.UtcTime(new Iec61850UtcTime(when, 0)),
            MmsDataValue.BitString(3, new byte[] { 0 }));
        var result = MmsSemanticReportValueProjector.Project(
            invalid, MmsReportSemanticProjectionContext.Create(model));

        Assert.Empty(result.Updates);
        Assert.Contains(result.Warnings,
            warning => warning.StartsWith("REPORT_SEMANTIC_REJECTED:", StringComparison.Ordinal) &&
                       warning.Contains("expected BitString, received UtcTime", StringComparison.Ordinal));
        // The original received frame remains available to diagnosis.
        Assert.Equal(4, invalid.Values[0].Value!.Children.Count);
    }

    [Fact]
    public void ExactTypedFcd_ValidOrderedQualityAndTimestampRetainProcessProjection()
    {
        const string root = "C264MEAS/MMXU1.A";
        const string dataSet = "C264MASTER/LLN0.Analog";
        var when = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
        var valid = BuildFrame(root, dataSet,
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(12f) }),
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(13f) }),
            MmsDataValue.BitString(3, new byte[] { 0 }),
            MmsDataValue.UtcTime(new Iec61850UtcTime(when, 0)));
        var result = MmsSemanticReportValueProjector.Project(
            valid, MmsReportSemanticProjectionContext.Create(BuildMeasurementPairModel(root, dataSet)));

        Assert.Contains(result.Updates, x => x.Reference == root + ".instMag.f" && x.HasValue);
        Assert.Contains(result.Updates, x => x.Reference == root + ".mag.f" && x.HasValue);
        Assert.DoesNotContain(result.Warnings, x => x.StartsWith("REPORT_SEMANTIC_REJECTED:", StringComparison.Ordinal));
    }

    [Fact]
    public void ConflictingExactSchemaAndPartialUnknownTypesStayDeterministic()
    {
        const string root = "C264MEAS/MMXU1.A";
        const string dataSet = "C264MASTER/LLN0.Analog";
        var contradiction = BuildModel(root, dataSet, "MV", new[]
        {
            Attribute(root + ".instMag.f", "instMag.f", "FLOAT32", "floating-point"),
            Attribute(root + ".instMag.f", "instMag.f", "BOOLEAN", "boolean")
        });
        var frame = BuildFrame(root, dataSet,
            MmsDataValue.Structure(new[] { MmsDataValue.FloatingPoint(12f) }));
        var rejected = MmsSemanticReportValueProjector.Project(
            frame, MmsReportSemanticProjectionContext.Create(contradiction));
        Assert.Empty(rejected.Updates);
        Assert.Contains(rejected.Warnings, x => x.StartsWith("REPORT_SEMANTIC_REJECTED:", StringComparison.Ordinal));

        var unknown = BuildModel(root, dataSet, "MV", new[]
        {
            new LiveIedDataAttributeModel
            {
                ObjectReference = root + ".instMag.f",
                AttributePath = "instMag.f",
                FunctionalConstraint = "MX",
                MmsType = "",
                TypeConfidence = LiveIedDiscoveryConfidenceLevel.Unknown
            }
        });
        var fallback = MmsSemanticReportValueProjector.Project(
            frame, MmsReportSemanticProjectionContext.Create(unknown));
        Assert.Contains(fallback.Updates, x => x.Reference == root + ".instMag.f");
        Assert.DoesNotContain(fallback.Warnings, x => x.StartsWith("REPORT_SEMANTIC_REJECTED:", StringComparison.Ordinal));
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
        return BuildModel(objectReference, dataSetReference, "WYE", attributes);
    }

    private static LiveIedModelDiscoveryDocument BuildMeasurementPairModel(
        string objectReference,
        string dataSetReference)
    {
        var attributes = new[]
        {
            Attribute(objectReference + ".instMag.f", "instMag.f"),
            Attribute(objectReference + ".mag.f", "mag.f"),
            Attribute(objectReference + ".q", "q", "Quality", "bit-string"),
            Attribute(objectReference + ".t", "t", "Timestamp", "utc-time")
        };
        return BuildModel(objectReference, dataSetReference, "MV", attributes);
    }

    private static LiveIedModelDiscoveryDocument BuildModel(
        string objectReference,
        string dataSetReference,
        string cdc,
        IReadOnlyList<LiveIedDataAttributeModel> attributes)
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
            IedName = "AA1E1F06R4",
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
                            LnClass = logicalNode.Contains("MHAI", StringComparison.OrdinalIgnoreCase) ? "MHAI" : "MMXU",
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
                    Reference = dataSetReference,
                    Domain = dataSetReference.Split('/')[0],
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
                            MmsReference = objectReference.Replace('.', '$'),
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }
                    }
                }
            }
        };
    }

    private static LiveIedDataAttributeModel Attribute(
        string reference,
        string path,
        string sclBType = "FLOAT32",
        string mmsType = "floating-point")
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "MX",
            MmsReference = reference.Replace('.', '$'),
            SclBType = sclBType,
            MmsType = mmsType,
            Source = "SCL.DataTypeTemplates",
            TypeSource = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static MmsReportFrame BuildFrame(
        string memberReference,
        string dataSetReference,
        params MmsDataValue[] phases)
        => BuildFrame(memberReference, dataSetReference, 0, phases);

    private static MmsReportFrame BuildFrame(
        string memberReference,
        string dataSetReference,
        int reportValueIndex,
        params MmsDataValue[] phases)
        => new()
        {
            ReceivedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            Header = new MmsReportHeader { DataSetReference = dataSetReference },
            Values = new[]
            {
                new MmsReportValue
                {
                    Index = reportValueIndex,
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
