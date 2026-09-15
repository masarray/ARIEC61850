using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Engineering.Runtime;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class CanonicalRuntimeValueAdapterTests
{
    [Fact]
    public void Initial_Read_Projects_Value_Quality_And_Timestamp_Into_Primary_Runtime_Row()
    {
        var plane = BuildPlane();
        var execution = new InitialFcReadExecutionResult
        {
            Batches =
            [
                new InitialFcReadBatchExecution
                {
                    Projections =
                    [
                        new InitialFcValueProjectionResult
                        {
                            Leaves =
                            [
                                Leaf("IED_ALD0/LLN0.Mod.stVal", "stVal", MmsDataValue.Boolean(true)),
                                Leaf("IED_ALD0/LLN0.Mod.q", "q", MmsDataValue.BitString(3, [0x00, 0x00])),
                                Leaf("IED_ALD0/LLN0.Mod.t", "t", MmsDataValue.UtcTime(new Iec61850UtcTime(
                                    new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero),
                                    0)))
                            ]
                        }
                    ]
                }
            ]
        };

        var result = CanonicalMmsRuntimeValueAdapter.ApplyInitialFcRead(plane, execution);
        var primary = QueryOne(plane, "IED_ALD0/LLN0.Mod.stVal");

        Assert.True(result.IsComplete, string.Join(" | ", result.Diagnostics));
        Assert.Equal("true", primary.Value);
        Assert.Equal("good", primary.Quality);
        Assert.Contains("2026-09-15", primary.Timestamp, StringComparison.Ordinal);
        Assert.Equal("initial-read", primary.Source);
    }

    [Fact]
    public void Quality_Only_Report_Merges_Without_Destroying_Last_Primary_Value()
    {
        var plane = BuildPlane();
        Assert.True(CanonicalMmsRuntimeValueAdapter.ApplyRead(
            plane,
            "IED_ALD0/LLN0.Mod.stVal",
            "ST",
            new MmsReadResult { IsSuccess = true, Value = MmsDataValue.Boolean(true) },
            "poll").IsComplete);

        var projection = new MmsReportValueProjection
        {
            Updates =
            [
                new MmsReportSignalUpdate
                {
                    Reference = "IED_ALD0/LLN0.Mod",
                    FunctionalConstraint = "ST",
                    Quality = "questionable",
                    Reason = "quality-change",
                    Source = "report",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    HasValue = false,
                    HasQuality = true
                }
            ]
        };

        var result = CanonicalMmsRuntimeValueAdapter.ApplyReportProjection(plane, projection);
        var primary = QueryOne(plane, "IED_ALD0/LLN0.Mod.stVal");

        Assert.True(result.IsComplete, string.Join(" | ", result.Diagnostics));
        Assert.Equal("true", primary.Value);
        Assert.Equal("questionable", primary.Quality);
        Assert.Equal("quality-change", primary.Reason);
        Assert.Equal("report", primary.Source);
    }

    [Fact]
    public void Polling_Read_Updates_Only_Runtime_Plane_And_Preserves_Model_Generation()
    {
        var plane = BuildPlane(modelGeneration: 23);
        var result = CanonicalMmsRuntimeValueAdapter.ApplyRead(
            plane,
            "IED_ALD0/LLN0.Mod.stVal",
            "ST",
            new MmsReadResult { IsSuccess = true, Value = MmsDataValue.Boolean(false) },
            "poll");

        var row = QueryOne(plane, "IED_ALD0/LLN0.Mod.stVal");
        Assert.True(result.IsComplete, string.Join(" | ", result.Diagnostics));
        Assert.Equal(23, plane.ModelGeneration);
        Assert.Equal("false", row.Value);
        Assert.Equal("poll", row.Source);
    }

    private static InitialFcProjectedLeaf Leaf(string reference, string path, MmsDataValue value)
        => new()
        {
            Reference = reference,
            AttributePath = path,
            FunctionalConstraint = "ST",
            SclBType = path switch
            {
                "q" => "Quality",
                "t" => "Timestamp",
                _ => "BOOLEAN"
            },
            Value = value
        };

    private static CanonicalRuntimeSignalProjection QueryOne(CanonicalRuntimeValuePlane plane, string reference)
        => Assert.Single(plane.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = reference,
            FunctionalConstraint = "ST",
            Limit = 10
        }).Rows.Where(row => string.Equals(row.Reference, reference, StringComparison.Ordinal)));

    private static CanonicalRuntimeValuePlane BuildPlane(long modelGeneration = 1)
    {
        var document = new LiveIedModelDiscoveryDocument
        {
            Source = "LiveMmsDiscovery",
            Host = "192.0.2.10",
            Port = 102,
            IedName = "IED_A",
            AccessPointName = "P1",
            IedIdentity = new LiveIedIdentity
            {
                IedName = "IED_A",
                Source = "MmsDomainCommonPrefix",
                Confidence = LiveIedDiscoveryConfidenceLevel.High
            },
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED_ALD0",
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "LLN0",
                            LnClass = "LLN0",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IED_ALD0/LLN0.Mod",
                                    Name = "Mod",
                                    InferredCdc = "ENC",
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                                    Attributes =
                                    [
                                        Attribute("IED_ALD0/LLN0.Mod.stVal", "stVal", "BOOLEAN"),
                                        Attribute("IED_ALD0/LLN0.Mod.q", "q", "Quality"),
                                        Attribute("IED_ALD0/LLN0.Mod.t", "t", "Timestamp")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var model = CanonicalLiveModelAdapter.FromLiveDiscovery(document);
        var snapshot = new CanonicalPublishedSnapshot
        {
            Generation = modelGeneration,
            Model = model,
            QueryIndex = CanonicalSignalQueryIndex.Build(model)
        };
        return new CanonicalRuntimeValuePlane(snapshot);
    }

    private static LiveIedDataAttributeModel Attribute(string reference, string path, string type)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "ST",
            SclBType = type,
            MmsType = type,
            MmsTypeSignature = type,
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
}
