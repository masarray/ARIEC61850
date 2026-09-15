using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Engineering.Runtime;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalRuntimePublicationTests
{
    [Fact]
    public async Task Publisher_Atomically_Rebinds_An_Empty_Value_Plane_Per_Model_Generation()
    {
        var model = BuildModel();
        await using var publisher = new CanonicalRuntimeSnapshotPublisher();

        Assert.True(publisher.TryPublish(model));
        var first = await WaitForGenerationAsync(publisher, 1);
        Assert.True(first.Values.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Value = "true",
            Source = "poll",
            HasValue = true
        }).IsApplied);
        Assert.Equal("true", Assert.Single(publisher.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Limit = 1
        }).Rows).Value);

        Assert.True(publisher.TryPublish(model));
        var second = await WaitForGenerationAsync(publisher, 2);
        Assert.NotSame(first.Values, second.Values);
        Assert.Equal(2, second.ModelGeneration);
        Assert.False(Assert.Single(publisher.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Limit = 1
        }).Rows).HasValue);

        await publisher.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(publisher.TryPublish(model));
    }

    [Fact]
    public async Task Accepted_Model_Replacement_Never_Leaves_Superseded_Runtime_Generation_Eligible_For_Update()
    {
        var model = BuildModel();
        await using var publisher = new CanonicalRuntimeSnapshotPublisher();

        Assert.True(publisher.TryPublish(model));
        var first = await WaitForGenerationAsync(publisher, 1);
        Assert.True(first.Values.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Value = "old-generation",
            Source = "poll",
            HasValue = true
        }).IsApplied);

        Assert.True(publisher.TryPublish(model));
        var immediatelyVisible = publisher.Current;

        Assert.True(
            immediatelyVisible is null || immediatelyVisible.ModelGeneration > first.ModelGeneration,
            "After a replacement is accepted, consumers must observe either no runtime source or the replacement generation, never the superseded value plane.");

        var second = await WaitForGenerationAsync(publisher, 2);
        Assert.False(Assert.Single(second.Values.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Limit = 1
        }).Rows).HasValue);
    }

    [Fact]
    public async Task Live_Ingress_Publishes_Only_Canonical_Model_And_Runtime_Consumer_Page()
    {
        var source = BuildDocument();
        await using var publisher = new CanonicalRuntimeSnapshotPublisher();

        Assert.True(CanonicalRuntimeIngressPublication.TryPublishLiveDiscovery(publisher, source));
        var current = await WaitForGenerationAsync(publisher, 1);
        var page = publisher.Query(new CanonicalSignalQuery { Limit = 10 });

        Assert.Equal(source.IedName, current.ModelSnapshot.Model.Identity.Name);
        Assert.Equal(1, page.ModelGeneration);
        Assert.Single(page.Rows);
    }

    private static async Task<CanonicalRuntimePublishedSnapshot> WaitForGenerationAsync(
        CanonicalRuntimeSnapshotPublisher publisher,
        long generation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((publisher.Current?.ModelGeneration ?? 0) < generation && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        var current = Assert.IsType<CanonicalRuntimePublishedSnapshot>(publisher.Current);
        Assert.Equal(generation, current.ModelGeneration);
        return current;
    }

    private static CanonicalIedModel BuildModel()
        => CanonicalLiveModelAdapter.FromLiveDiscovery(BuildDocument());

    private static LiveIedModelDiscoveryDocument BuildDocument()
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
                                            MmsType = "boolean",
                                            MmsTypeSignature = "boolean",
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
}
