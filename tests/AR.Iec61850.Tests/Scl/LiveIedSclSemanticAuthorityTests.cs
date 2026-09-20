using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class LiveIedSclSemanticAuthorityTests
{
    [Fact]
    public void ExactLiveIntegerStatus_RepairsStaleBooleanCdc_AndRoundTripsAsInsInt32()
    {
        var model = new LiveIedModelDiscoveryDocument
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
                                    Reference = "IEDADD/GGIO1.CBClsCounter",
                                    Name = "CBClsCounter",
                                    ProposedDoTypeId = "DO_SPS_GGIO_CBClsCounter",
                                    // Deliberately stale to prove the export boundary
                                    // cannot serialize an exact integer status as SPS.
                                    InferredCdc = "SPS",
                                    CdcConfidence = 0.38,
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Medium,
                                    Attributes =
                                    [
                                        Attribute("CBClsCounter", "stVal", "INT32", "integer"),
                                        Attribute("CBClsCounter", "q", "Quality", "bit-string"),
                                        Attribute("CBClsCounter", "t", "Timestamp", "utc-time")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var document = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions
            {
                Profile = "full-model"
            });

        AssertExportedCounterIsInsInt32(document);

        var reopened = SclLiveModelProjectionBuilder.Build(
            document,
            "generated.iid");
        var counter = Assert.Single(
            reopened.LogicalDevices
                .SelectMany(device => device.LogicalNodes)
                .SelectMany(node => node.DataObjects),
            dataObject => dataObject.Name == "CBClsCounter");

        Assert.Equal("INS", counter.InferredCdc);
        var stVal = Assert.Single(
            counter.Attributes,
            attribute => attribute.AttributePath == "stVal");
        Assert.Equal("INT32", stVal.SclBType);
        Assert.Equal(
            LiveIedDiscoveryConfidenceLevel.Exact,
            stVal.TypeConfidence);
    }

    [Fact]
    public void ExactLiveBooleanStatus_RemainsSpsBooleanAcrossExportAndReopen()
    {
        var model = BuildStatusModel(
            dataObjectName: "BinaryStatus",
            declaredCdc: "SPS",
            stValBType: "BOOLEAN",
            stValMmsType: "boolean");

        var document = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions
            {
                Profile = "full-model"
            });

        var reopened = SclLiveModelProjectionBuilder.Build(
            document,
            "generated.iid");
        var status = Assert.Single(
            reopened.LogicalDevices
                .SelectMany(device => device.LogicalNodes)
                .SelectMany(node => node.DataObjects),
            dataObject => dataObject.Name == "BinaryStatus");

        Assert.Equal("SPS", status.InferredCdc);
        Assert.Equal(
            "BOOLEAN",
            Assert.Single(
                status.Attributes,
                attribute => attribute.AttributePath == "stVal").SclBType);
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
                                    CdcConfidence = 0.98,
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
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
            Source = "LiveMmsTypeSpecification",
            SclBType = sclBType,
            MmsType = mmsType,
            TypeDiscoveryStatus = "Exact",
            TypeSource = "LiveMmsTypeSpecification",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
            FunctionalConstraintConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static void AssertExportedCounterIsInsInt32(XDocument document)
    {
        var ns = document.Root!.Name.Namespace;
        var lNodeType = Assert.Single(
            document.Descendants(ns + "LNodeType"),
            element => (string?)element.Attribute("lnClass") == "GGIO");
        var dataObject = Assert.Single(
            lNodeType.Elements(ns + "DO"),
            element => (string?)element.Attribute("name") == "CBClsCounter");
        var doTypeId = (string?)dataObject.Attribute("type") ?? string.Empty;
        var doType = Assert.Single(
            document.Descendants(ns + "DOType"),
            element => (string?)element.Attribute("id") == doTypeId);

        Assert.Equal("INS", (string?)doType.Attribute("cdc"));

        var stVal = Assert.Single(
            doType.Elements(ns + "DA"),
            element => (string?)element.Attribute("name") == "stVal");
        Assert.Equal("INT32", (string?)stVal.Attribute("bType"));
    }
}
