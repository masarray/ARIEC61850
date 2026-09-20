using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class LiveIedSclExportOnlySemanticAuthorityTests
{
    [Fact]
    public void ExactLiveIntegerStatus_CorrectsOnlyExportedScl_AndRoundTripsAsInsInt32()
    {
        var model = BuildStatusModel(
            dataObjectName: "CBClsCounter",
            declaredCdc: "SPS",
            stValBType: "INT32",
            stValMmsType: "integer");

        // Runtime/live-discovery authority intentionally remains the proven
        // conservative model. The correction is applied only at SCL export.
        var live = Assert.Single(
            model.LogicalDevices
                .SelectMany(device => device.LogicalNodes)
                .SelectMany(node => node.DataObjects));
        Assert.Equal("SPS", live.InferredCdc);

        var document = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions { Profile = "full-model" });

        AssertExportedStatus(document, "CBClsCounter", "INS", "INT32");

        var reopened = SclLiveModelProjectionBuilder.Build(document, "generated.iid");
        var counter = Assert.Single(
            reopened.LogicalDevices
                .SelectMany(device => device.LogicalNodes)
                .SelectMany(node => node.DataObjects),
            dataObject => dataObject.Name == "CBClsCounter");

        Assert.Equal("INS", counter.InferredCdc);
        Assert.Equal(
            "INT32",
            Assert.Single(counter.Attributes, attribute => attribute.AttributePath == "stVal").SclBType);

        // Export must not mutate the live model that remains bound to runtime/reporting.
        Assert.Equal("SPS", live.InferredCdc);
    }

    [Fact]
    public void ExactLiveBitStringStatus_DoesNotRewriteRuntimeOrExportedGenericSps()
    {
        var model = BuildStatusModel(
            dataObjectName: "SwLoc",
            declaredCdc: "SPS",
            stValBType: "Check",
            stValMmsType: "bit-string");

        var document = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions { Profile = "full-model" });

        // BIT STRING is not proof of Boolean vs integer status semantics. Keep
        // the proven conservative SPS export instead of inventing a new CDC.
        AssertExportedStatus(document, "SwLoc", "SPS", "BOOLEAN");
        Assert.Equal(
            "SPS",
            Assert.Single(
                model.LogicalDevices
                    .SelectMany(device => device.LogicalNodes)
                    .SelectMany(node => node.DataObjects)).InferredCdc);
    }

    [Fact]
    public void StrongerSemanticCdc_IsNeverOverriddenByPrimitiveLeafType()
    {
        var model = BuildStatusModel(
            dataObjectName: "Op",
            declaredCdc: "ACT",
            stValBType: "BOOLEAN",
            stValMmsType: "boolean");

        var document = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions { Profile = "full-model" });

        AssertExportedStatus(document, "Op", "ACT", "BOOLEAN");
    }

    private static LiveIedModelDiscoveryDocument BuildStatusModel(
        string dataObjectName,
        string declaredCdc,
        string stValBType,
        string stValMmsType)
        => new()
        {
            IedName = "IED",
            AccessPointName = "AP1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDADD",
                    Inst = "ADD",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "GGIO1",
                            LnClass = "GGIO",
                            LnInst = "1",
                            ProposedLnTypeId = "LN_GGIO_GGIO1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDADD/GGIO1." + dataObjectName,
                                    Name = dataObjectName,
                                    ProposedDoTypeId = "DO_" + declaredCdc + "_GGIO_" + dataObjectName,
                                    InferredCdc = declaredCdc,
                                    CdcConfidence = 0.78,
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Medium,
                                    Attributes =
                                    [
                                        Attribute(dataObjectName, "stVal", stValBType, stValMmsType),
                                        Attribute(dataObjectName, "q", "Quality", "bit-string"),
                                        Attribute(dataObjectName, "t", "Timestamp", "utc-time")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

    private static LiveIedDataAttributeModel Attribute(
        string dataObjectName,
        string path,
        string sclBType,
        string mmsType)
        => new()
        {
            ObjectReference = "IEDADD/GGIO1." + dataObjectName + "." + path,
            AttributePath = path,
            FunctionalConstraint = "ST",
            MmsReference = "IEDADD/GGIO1$ST$" + dataObjectName + "$" + path,
            MmsItemName = "GGIO1$ST$" + dataObjectName + "$" + path,
            Source = "GetVariableAccessAttributes",
            SclBType = sclBType,
            MmsType = mmsType,
            TypeDiscoveryStatus = "Exact",
            TypeSource = "GetVariableAccessAttributes",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
            FunctionalConstraintConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static void AssertExportedStatus(
        XDocument document,
        string dataObjectName,
        string expectedCdc,
        string expectedStValBType)
    {
        var ns = document.Root!.Name.Namespace;
        var lNodeType = Assert.Single(
            document.Descendants(ns + "LNodeType"),
            element => (string?)element.Attribute("lnClass") == "GGIO");
        var dataObject = Assert.Single(
            lNodeType.Elements(ns + "DO"),
            element => (string?)element.Attribute("name") == dataObjectName);
        var doTypeId = (string?)dataObject.Attribute("type") ?? string.Empty;
        var doType = Assert.Single(
            document.Descendants(ns + "DOType"),
            element => (string?)element.Attribute("id") == doTypeId);

        Assert.Equal(expectedCdc, (string?)doType.Attribute("cdc"));
        var stVal = Assert.Single(
            doType.Elements(ns + "DA"),
            element => (string?)element.Attribute("name") == "stVal");
        Assert.Equal(expectedStValBType, (string?)stVal.Attribute("bType"));
    }
}
