using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Scl;

public sealed class LiveIedSclRcbOptionFidelityTests
{
    [Fact]
    public void Authoritative_Exporter_Preserves_Gi_Only_And_BufferOverflow_Only()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            IedName = "OCR7SJ8",
            Host = "1.110.1.1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "OCR7SJ8Application",
                    Inst = "Application",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "LLN0",
                            LnClass = "LLN0",
                            ProposedLnTypeId = "LN_LLN0"
                        }
                    ]
                }
            ],
            DataSets =
            [
                new LiveIedDataSetModel
                {
                    Reference = "OCR7SJ8Application/LLN0.DataSet",
                    Domain = "OCR7SJ8Application",
                    LogicalNode = "LLN0",
                    Name = "DataSet",
                    MemberCount = 1,
                    Members =
                    [
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = "OCR7SJ8Application/LLN0.Mod.stVal",
                            FunctionalConstraint = "ST"
                        }
                    ]
                }
            ],
            ReportControls =
            [
                new LiveIedReportControlModel
                {
                    Reference = "OCR7SJ8Application/LLN0.RP.urcbD010101",
                    Domain = "OCR7SJ8Application",
                    LogicalNode = "LLN0",
                    Name = "urcbD010101",
                    Buffered = false,
                    DataSetReference = "OCR7SJ8Application/LLN0.DataSet",
                    ConfRev = "1",
                    TriggerOptions = "bits(08, unused=2)",
                    OptionalFields = "bits(0200, unused=6)"
                }
            ]
        };
        var options = new LiveIedSclExportOptions
        {
            Profile = "full-model",
            SchemaProfile = SclSchemaProfile.Edition2V31,
            IedNameOverride = "OCR7SJ8"
        };

        var generated = LiveIedSclExporter.BuildDocument(model, options);
        var document = AuthoritativeLiveIedSclExporter.ApplyReportControlConfiguration(
            generated,
            model,
            options.ResolvedSchemaProfile);

        var root = Assert.IsType<XElement>(document.Root);
        var ns = root.Name.Namespace;
        var report = Assert.Single(root.Descendants(ns + "ReportControl"));
        var trigger = Assert.Single(report.Elements(ns + "TrgOps"));
        Assert.Equal("false", (string?)trigger.Attribute("dchg"));
        Assert.Equal("false", (string?)trigger.Attribute("qchg"));
        Assert.Equal("false", (string?)trigger.Attribute("dupd"));
        Assert.Equal("false", (string?)trigger.Attribute("period"));
        Assert.Equal("true", (string?)trigger.Attribute("gi"));

        var optional = Assert.Single(report.Elements(ns + "OptFields"));
        Assert.Equal("false", (string?)optional.Attribute("seqNum"));
        Assert.Equal("false", (string?)optional.Attribute("timeStamp"));
        Assert.Equal("false", (string?)optional.Attribute("reasonCode"));
        Assert.Equal("false", (string?)optional.Attribute("dataSet"));
        Assert.Equal("false", (string?)optional.Attribute("dataRef"));
        Assert.Equal("true", (string?)optional.Attribute("bufOvfl"));
        Assert.Equal("false", (string?)optional.Attribute("entryID"));
        Assert.Equal("false", (string?)optional.Attribute("configRef"));
        Assert.Equal("false", (string?)optional.Attribute("segmentation"));
    }
}