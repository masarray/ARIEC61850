using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Engineering.Runtime;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalSnapshotPublicationTests
{
    [Fact]
    public void Memory_Compactor_Reuses_Hot_Tables_And_Unifies_Metadata_Strings()
    {
        var source = BuildCanonicalModel();
        var domainA = Copy("IED_ALD0");
        var domainB = Copy("IED_ALD0");
        Assert.NotSame(domainA, domainB);

        var model = CloneWithMetadata(source,
            new CanonicalDataSet
            {
                Reference = Copy("IED_ALD0/LLN0$Status"),
                MmsDomain = domainA,
                LogicalNode = Copy("LLN0"),
                Name = Copy("Status")
            },
            new CanonicalReportControl
            {
                Reference = Copy("IED_ALD0/LLN0$RP$Status01"),
                MmsDomain = domainB,
                LogicalNode = Copy("LLN0"),
                Name = Copy("Status01"),
                DataSetReference = Copy("IED_ALD0/LLN0$Status"),
                Provenance = new CanonicalProvenance(CanonicalEvidenceSource.LiveMms, CanonicalConfidence.Exact)
            });

        var compacted = CanonicalModelMemoryCompactor.Compact(model);

        Assert.Same(model.Signals, compacted.Signals);
        Assert.Same(model.DataObjects, compacted.DataObjects);
        Assert.Same(compacted.DataSets[0].MmsDomain, compacted.ReportControls[0].MmsDomain);
        Assert.Same(compacted.DataSets[0].Reference, compacted.ReportControls[0].DataSetReference);
        Assert.Equal(model.SignalCount, compacted.SignalCount);
    }

    [Fact]
    public void Query_Index_Uses_Ordinal_Table_And_Returns_Bounded_Reference_Pages()
    {
        var model = BuildCanonicalModel();
        var index = CanonicalSignalQueryIndex.Build(model);

        Assert.Equal(model.SignalCount, index.SignalCount);
        Assert.Equal((long)model.SignalCount * sizeof(int), index.ApproximateIndexBytes);

        var result = index.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = "IED_ALD0/LLN0.Mod",
            FunctionalConstraint = "ST",
            Limit = 1
        }, generation: 7);

        Assert.Equal(7, result.Generation);
        Assert.Single(result.Rows);
        Assert.True(result.HasMore);
        Assert.StartsWith("IED_ALD0/LLN0.Mod", result.Rows[0].Reference);
        Assert.Equal("ST", result.Rows[0].FunctionalConstraint);
    }

    [Fact]
    public async Task Publisher_Exposes_Only_Immutable_Latest_Snapshot_Through_Bounded_Consumer_Pages()
    {
        var model = BuildCanonicalModel();
        await using var publisher = new CanonicalSnapshotPublisher();

        Assert.True(publisher.TryPublish(model));
        await WaitForPublicationAsync(publisher);

        var current = Assert.IsType<CanonicalPublishedSnapshot>(publisher.Current);
        Assert.Equal(1, current.Generation);
        Assert.Equal(model.SignalCount, current.Model.SignalCount);

        var ui = CanonicalConsumerProjection.ForUi(
            publisher,
            referencePrefix: "IED_ALD0/LLN0.Mod",
            functionalConstraint: "ST",
            limit: 1);
        Assert.Single(ui.Rows);
        Assert.True(ui.HasMore);
        Assert.Equal(current.Generation, ui.Generation);

        await publisher.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(publisher.TryPublish(model));
    }

    private static async Task WaitForPublicationAsync(CanonicalSnapshotPublisher publisher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (publisher.Current is null && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.NotNull(publisher.Current);
    }

    private static CanonicalIedModel BuildCanonicalModel()
    {
        var source = new LiveIedModelDiscoveryDocument
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
                Confidence = LiveIedDiscoveryConfidenceLevel.High,
                CandidateNames = ["IED_A"],
                Evidence = ["domain consensus"]
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
                            ProposedLnTypeId = "LT_LLN0",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IED_ALD0/LLN0.Mod",
                                    Name = "Mod",
                                    ProposedDoTypeId = "DOT_Mod",
                                    InferredCdc = "ENC",
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                                    Attributes =
                                    [
                                        Attribute("IED_ALD0/LLN0.Mod.stVal", "stVal"),
                                        Attribute("IED_ALD0/LLN0.Mod.q", "q")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        return CanonicalLiveModelAdapter.FromLiveDiscovery(source);
    }

    private static CanonicalIedModel CloneWithMetadata(
        CanonicalIedModel source,
        CanonicalDataSet dataSet,
        CanonicalReportControl report)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            GeneratedAtUtc = source.GeneratedAtUtc,
            Identity = source.Identity,
            Communication = source.Communication,
            Source = source.Source,
            Strings = source.Strings,
            AccessPoints = source.AccessPoints,
            LogicalDevices = source.LogicalDevices,
            LogicalNodes = source.LogicalNodes,
            DataObjects = source.DataObjects,
            Signals = source.Signals,
            DataSets = [dataSet],
            ReportControls = [report],
            Diagnostics = source.Diagnostics
        };

    private static LiveIedDataAttributeModel Attribute(string reference, string path)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "ST",
            SclBType = "BOOLEAN",
            MmsType = "boolean",
            MmsTypeSignature = "boolean",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static string Copy(string value)
        => new(value.ToCharArray());
}
