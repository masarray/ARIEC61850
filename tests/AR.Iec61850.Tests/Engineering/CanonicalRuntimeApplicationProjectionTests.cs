using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Runtime;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalRuntimeApplicationProjectionTests
{
    [Fact]
    public async Task Monitor_Projection_Resolves_Exact_Pinned_Rows_From_One_Published_Generation()
    {
        await using var publisher = new CanonicalRuntimeSnapshotPublisher();
        Assert.True(CanonicalRuntimeIngressPublication.TryPublishLiveDiscovery(publisher, BuildDocument(4)));
        var current = await WaitForPublicationAsync(publisher);

        Assert.True(current.Values.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = "IED_ALD0/LLN0.Wide.v000002",
            FunctionalConstraint = "ST",
            Value = "42",
            Quality = "good",
            Source = "report",
            HasValue = true,
            HasQuality = true
        }).IsApplied);

        var projection = CanonicalRuntimeApplicationProjection.ForMonitor(
            publisher,
            [
                new CanonicalRuntimeSignalSelection("IED_ALD0/LLN0.Wide.v000002", "st"),
                new CanonicalRuntimeSignalSelection("IED_ALD0/LLN0.Wide.missing", "ST")
            ]);

        Assert.Equal(current.ModelGeneration, projection.ModelGeneration);
        Assert.Equal(current.Values.ValueGeneration, projection.ValueGeneration);
        var row = Assert.Single(projection.Rows);
        Assert.Equal("IED_ALD0/LLN0.Wide.v000002", row.Reference);
        Assert.Equal("42", row.Value);
        Assert.Equal("good", row.Quality);
        Assert.Single(projection.Missing);
        Assert.False(projection.WasTruncated);
    }

    [Fact]
    public async Task Monitor_Projection_Hard_Caps_Selection_Without_Full_Model_Materialization()
    {
        const int signalCount = 400;
        await using var publisher = new CanonicalRuntimeSnapshotPublisher();
        Assert.True(CanonicalRuntimeIngressPublication.TryPublishLiveDiscovery(publisher, BuildDocument(signalCount)));
        var current = await WaitForPublicationAsync(publisher);

        var selections = Enumerable.Range(0, signalCount)
            .Select(index => new CanonicalRuntimeSignalSelection(
                $"IED_ALD0/LLN0.Wide.v{index:D6}",
                "ST"))
            .ToArray();

        var projection = CanonicalRuntimeApplicationProjection.ForMonitor(publisher, selections);

        Assert.True(projection.WasTruncated);
        Assert.Equal(CanonicalRuntimeApplicationProjection.MaximumMonitorSelectionCount, projection.RequestedCount);
        Assert.Equal(CanonicalRuntimeApplicationProjection.MaximumMonitorSelectionCount, projection.Rows.Count);
        Assert.Equal(signalCount, current.ModelSnapshot.Model.SignalCount);
    }

    private static async Task<CanonicalRuntimePublishedSnapshot> WaitForPublicationAsync(CanonicalRuntimeSnapshotPublisher publisher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (publisher.Current is null && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        return Assert.IsType<CanonicalRuntimePublishedSnapshot>(publisher.Current);
    }

    private static LiveIedModelDiscoveryDocument BuildDocument(int signalCount)
        => new()
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
                                    Reference = "IED_ALD0/LLN0.Wide",
                                    Name = "Wide",
                                    InferredCdc = "GEN",
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                                    Attributes = Enumerable.Range(0, signalCount)
                                        .Select(index => new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = $"IED_ALD0/LLN0.Wide.v{index:D6}",
                                            AttributePath = $"v{index:D6}",
                                            FunctionalConstraint = "ST",
                                            SclBType = "INT32",
                                            MmsType = "integer",
                                            MmsTypeSignature = "integer",
                                            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
                                        })
                                        .ToArray()
                                }
                            ]
                        }
                    ]
                }
            ]
        };
}
