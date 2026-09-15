using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Runtime;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class CanonicalRuntimeApplicationAdapterTests
{
    [Fact]
    public async Task Successful_Persistent_Monitor_Poll_Updates_Current_Canonical_Runtime_Generation()
    {
        await using var publisher = new CanonicalRuntimeSnapshotPublisher();
        Assert.True(CanonicalRuntimeIngressPublication.TryPublishLiveDiscovery(publisher, BuildDocument()));
        var current = await WaitForPublicationAsync(publisher);
        var readAt = new DateTimeOffset(2026, 9, 15, 1, 2, 3, TimeSpan.Zero);

        var result = CanonicalMmsRuntimeApplicationAdapter.ApplyPollRead(
            publisher,
            new MmsReportPollRead
            {
                ReadAt = readAt,
                Reference = "IED_ALD0/LLN0.Mod.stVal",
                SelectedReference = "IED_ALD0/LLN0.Mod.stVal",
                FunctionalConstraint = "ST",
                IsSuccess = true,
                DisplayValue = "true",
                Message = "poll read complete"
            });

        Assert.True(result.IsComplete, string.Join(" | ", result.Diagnostics));
        var row = Assert.Single(current.Values.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Limit = 10
        }).Rows, row => string.Equals(row.Reference, "IED_ALD0/LLN0.Mod.stVal", StringComparison.Ordinal));
        Assert.Equal("true", row.Value);
        Assert.Equal("poll", row.Source);
        Assert.Equal(readAt, row.UpdatedAtUtc);
    }

    [Fact]
    public async Task Failed_Persistent_Monitor_Poll_Preserves_Last_Good_Canonical_Value()
    {
        await using var publisher = new CanonicalRuntimeSnapshotPublisher();
        Assert.True(CanonicalRuntimeIngressPublication.TryPublishLiveDiscovery(publisher, BuildDocument()));
        var current = await WaitForPublicationAsync(publisher);
        Assert.True(current.Values.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Value = "true",
            Source = "report",
            HasValue = true
        }).IsApplied);
        var generationBefore = current.Values.ValueGeneration;

        var result = CanonicalMmsRuntimeApplicationAdapter.ApplyPollRead(
            publisher,
            new MmsReportPollRead
            {
                SelectedReference = "IED_ALD0/LLN0.Mod.stVal",
                FunctionalConstraint = "ST",
                IsSuccess = false,
                Message = "temporary read failure"
            });

        Assert.False(result.IsComplete);
        Assert.Equal(generationBefore, current.Values.ValueGeneration);
        var row = Assert.Single(current.Values.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Limit = 10
        }).Rows, row => string.Equals(row.Reference, "IED_ALD0/LLN0.Mod.stVal", StringComparison.Ordinal));
        Assert.Equal("true", row.Value);
        Assert.Equal("report", row.Source);
    }

    private static async Task<CanonicalRuntimePublishedSnapshot> WaitForPublicationAsync(CanonicalRuntimeSnapshotPublisher publisher)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (publisher.Current is null && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        return Assert.IsType<CanonicalRuntimePublishedSnapshot>(publisher.Current);
    }

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
