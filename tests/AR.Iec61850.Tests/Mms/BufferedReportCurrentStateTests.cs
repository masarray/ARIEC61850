using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Engineering.Runtime;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class BufferedReportCurrentStateTests
{
    [Fact]
    public void Buffered_Backlog_Applied_In_Order_Leaves_Latest_Value_And_Preserves_It_On_Quality_Only_Update()
    {
        var plane = BuildPlane();

        var older = Projection(value: "false", quality: "good", reason: "data-change", hasValue: true, hasQuality: true);
        var newer = Projection(value: "true", quality: "good", reason: "data-update", hasValue: true, hasQuality: true);
        var qualityOnly = Projection(value: string.Empty, quality: "questionable", reason: "quality-change", hasValue: false, hasQuality: true);

        Assert.True(CanonicalMmsRuntimeValueAdapter.ApplyReportProjection(plane, older).IsComplete);
        Assert.True(CanonicalMmsRuntimeValueAdapter.ApplyReportProjection(plane, newer).IsComplete);

        var afterBacklog = QueryOne(plane);
        Assert.Equal("true", afterBacklog.Value);
        Assert.Equal("good", afterBacklog.Quality);
        Assert.Equal("data-update", afterBacklog.Reason);
        Assert.Equal("report", afterBacklog.Source);

        Assert.True(CanonicalMmsRuntimeValueAdapter.ApplyReportProjection(plane, qualityOnly).IsComplete);

        var afterQualityOnly = QueryOne(plane);
        Assert.Equal("true", afterQualityOnly.Value);
        Assert.Equal("questionable", afterQualityOnly.Quality);
        Assert.Equal("quality-change", afterQualityOnly.Reason);
        Assert.Equal("report", afterQualityOnly.Source);
    }

    private static MmsReportValueProjection Projection(
        string value,
        string quality,
        string reason,
        bool hasValue,
        bool hasQuality)
        => new()
        {
            Updates =
            [
                new MmsReportSignalUpdate
                {
                    Reference = "IED_ALD0/LLN0.Mod.stVal",
                    FunctionalConstraint = "ST",
                    Value = value,
                    Quality = quality,
                    Reason = reason,
                    Source = "report",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    HasValue = hasValue,
                    HasQuality = hasQuality
                }
            ]
        };

    private static CanonicalRuntimeSignalProjection QueryOne(CanonicalRuntimeValuePlane plane)
        => Assert.Single(
            plane.Query(new CanonicalSignalQuery
            {
                ReferencePrefix = "IED_ALD0/LLN0.Mod.stVal",
                FunctionalConstraint = "ST",
                Limit = 10
            }).Rows,
            row => string.Equals(row.Reference, "IED_ALD0/LLN0.Mod.stVal", StringComparison.Ordinal));

    private static CanonicalRuntimeValuePlane BuildPlane()
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
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "IED_ALD0/LLN0.Mod.stVal",
                                            AttributePath = "stVal",
                                            FunctionalConstraint = "ST",
                                            SclBType = "BOOLEAN",
                                            MmsType = "BOOLEAN",
                                            MmsTypeSignature = "BOOLEAN",
                                            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var model = CanonicalLiveModelAdapter.FromLiveDiscovery(document);
        return new CanonicalRuntimeValuePlane(new CanonicalPublishedSnapshot
        {
            Generation = 1,
            Model = model,
            QueryIndex = CanonicalSignalQueryIndex.Build(model)
        });
    }
}
