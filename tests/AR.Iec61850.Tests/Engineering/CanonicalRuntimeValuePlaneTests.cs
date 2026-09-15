using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Engineering.Runtime;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalRuntimeValuePlaneTests
{
    [Fact]
    public void Runtime_Updates_Do_Not_Rebuild_Or_Mutate_Static_Canonical_Model()
    {
        var model = BuildModel();
        var signals = model.Signals;
        var snapshot = Snapshot(model, generation: 11);
        var plane = new CanonicalRuntimeValuePlane(snapshot);

        var applied = plane.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Value = "on",
            Source = "poll",
            HasValue = true
        });

        Assert.True(applied.IsApplied, applied.Message);
        Assert.Same(signals, model.Signals);
        Assert.Same(model, plane.ModelSnapshot.Model);
        Assert.Equal(11, plane.ModelGeneration);
        Assert.Equal(1, plane.ValueGeneration);

        var row = Assert.Single(plane.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Limit = 10
        }).Rows);
        Assert.Equal("on", row.Value);
        Assert.True(row.HasValue);
        Assert.Equal("poll", row.Source);
    }

    [Fact]
    public void Companion_Quality_Update_Enriches_Primary_Child_Without_Overwriting_Value()
    {
        var plane = new CanonicalRuntimeValuePlane(Snapshot(BuildModel()));
        Assert.True(plane.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Value = "true",
            Source = "initial-read",
            HasValue = true
        }).IsApplied);

        var companion = plane.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = "IED_ALD0/LLN0.Mod",
            FunctionalConstraint = "ST",
            Quality = "good",
            Reason = "quality-change",
            Source = "report",
            HasQuality = true,
            HasReason = true
        });

        Assert.Equal(CanonicalRuntimeApplyStatus.AppliedToCompanionChildren, companion.Status);
        Assert.Equal(1, companion.AppliedSignalCount);

        var row = Assert.Single(plane.Query(new CanonicalSignalQuery
        {
            ReferencePrefix = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Limit = 10
        }).Rows);
        Assert.Equal("true", row.Value);
        Assert.Equal("good", row.Quality);
        Assert.Equal("quality-change", row.Reason);
        Assert.Equal("report", row.Source);
    }

    [Fact]
    public void Runtime_Consumer_Page_Caps_Are_Enforced_Without_Second_Model_Graph()
    {
        var model = BuildWideModel(1_501);
        var plane = new CanonicalRuntimeValuePlane(Snapshot(model));

        var ui = CanonicalRuntimeConsumerProjection.ForUi(plane, limit: 9_999);
        var cli = CanonicalRuntimeConsumerProjection.ForCli(plane, limit: 9_999);

        Assert.Equal(CanonicalRuntimeConsumerProjection.MaximumUiPageSize, ui.Rows.Count);
        Assert.True(ui.HasMore);
        Assert.Equal(1_501, cli.Rows.Count);
        Assert.False(cli.HasMore);
        Assert.Equal(model.SignalCount, plane.SignalCount);
    }

    [Fact]
    public void Csv_Exporter_Streams_Canonical_Model_And_Runtime_Overlay_With_Generation_Evidence()
    {
        var plane = new CanonicalRuntimeValuePlane(Snapshot(BuildModel(), generation: 17));
        Assert.True(plane.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = "IED_ALD0/LLN0.Mod.stVal",
            FunctionalConstraint = "ST",
            Value = "true",
            Quality = "good",
            Source = "report",
            HasValue = true,
            HasQuality = true
        }).IsApplied);

        using var writer = new StringWriter();
        var exported = CanonicalRuntimeCsvExporter.Write(plane, writer, pageSize: 2);
        var csv = writer.ToString();

        Assert.Equal(17, exported.ModelGeneration);
        Assert.Equal(exported.StartValueGeneration, exported.EndValueGeneration);
        Assert.False(exported.ValuesChangedDuringExport);
        Assert.Equal(3, exported.RowCount);
        Assert.Contains("SignalId,Reference,FC", csv, StringComparison.Ordinal);
        Assert.Contains("IED_ALD0/LLN0.Mod.stVal", csv, StringComparison.Ordinal);
        Assert.Contains("true,good", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void One_Million_Signal_Runtime_Overlay_Has_Fixed_Primitive_Array_Cost()
    {
        const int signalCount = 1_000_000;
        const long allocationBudget = 64L * 1024L * 1024L;
        var rows = new CanonicalSignalRow[signalCount];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = new CanonicalSignalRow(
                index,
                0,
                CanonicalSymbol.Empty,
                CanonicalSymbol.Empty,
                CanonicalSymbol.Empty,
                CanonicalSymbol.Empty,
                CanonicalSymbol.Empty,
                CanonicalSymbol.Empty,
                new CanonicalProvenance(CanonicalEvidenceSource.LiveMms, CanonicalConfidence.Exact));
        }

        var model = new CanonicalIedModel { Signals = rows };
        var snapshot = new CanonicalPublishedSnapshot
        {
            Generation = 1,
            Model = model,
            QueryIndex = null!
        };

        var before = GC.GetAllocatedBytesForCurrentThread();
        var plane = new CanonicalRuntimeValuePlane(snapshot);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(40_000_000L, plane.ApproximateBackingBytes);
        Assert.True(
            allocated < allocationBudget,
            $"One-million-signal runtime overlay allocated {allocated:N0} bytes; budget is {allocationBudget:N0} bytes.");
    }

    private static CanonicalPublishedSnapshot Snapshot(CanonicalIedModel model, long generation = 1)
        => new()
        {
            Generation = generation,
            Model = model,
            QueryIndex = CanonicalSignalQueryIndex.Build(model)
        };

    private static CanonicalIedModel BuildModel()
        => CanonicalLiveModelAdapter.FromLiveDiscovery(BuildDocument(
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
        ]));

    private static CanonicalIedModel BuildWideModel(int signalCount)
        => CanonicalLiveModelAdapter.FromLiveDiscovery(BuildDocument(
        [
            new LiveIedDataObjectModel
            {
                Reference = "IED_ALD0/LLN0.Wide",
                Name = "Wide",
                InferredCdc = "GEN",
                ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                Attributes = Enumerable.Range(0, signalCount)
                    .Select(index => Attribute(
                        $"IED_ALD0/LLN0.Wide.v{index:D6}",
                        $"v{index:D6}",
                        "INT32"))
                    .ToArray()
            }
        ]));

    private static LiveIedModelDiscoveryDocument BuildDocument(IReadOnlyList<LiveIedDataObjectModel> dataObjects)
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
                            DataObjects = dataObjects
                        }
                    ]
                }
            ]
        };

    private static LiveIedDataAttributeModel Attribute(string reference, string path, string bType)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "ST",
            SclBType = bType,
            MmsType = bType,
            MmsTypeSignature = bType,
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
}
