using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Export;
using System.Xml.Linq;

namespace AR.Iec61850.Tests.Scl;

public sealed class LiveIedSclIedIdentityTests
{
    [Fact]
    public void Exporter_Uses_Authoritative_Ied_Name_And_Preserves_Explicit_Mms_Domains()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "1.110.1.1",
            // Reproduces the unsafe legacy heuristic: the first MMS domain was copied as IED name.
            IedName = "OCR7SJ8Application",
            AccessPointName = "AP1",
            LogicalDevices =
            [
                LogicalDevice("OCR7SJ8Application"),
                LogicalDevice("OCR7SJ8CB1")
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
                            Reference = "OCR7SJ8CB1/XCBR1.Pos.stVal",
                            FunctionalConstraint = "ST"
                        }
                    ]
                }
            ],
            ReportControls =
            [
                new LiveIedReportControlModel
                {
                    Reference = "OCR7SJ8Application/LLN0.RP.urcbD0101",
                    Domain = "OCR7SJ8Application",
                    LogicalNode = "LLN0",
                    Name = "urcbD0101",
                    Buffered = false,
                    DataSetReference = "OCR7SJ8Application/LLN0.DataSet",
                    ConfRev = "1"
                }
            ]
        };

        var generated = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions
            {
                Profile = "full-model",
                SchemaProfile = SclSchemaProfile.Edition1V16,
                IpAddress = "1.110.1.1"
            });
        var document = AuthoritativeLiveIedSclExporter.ApplyIdentity(
            generated,
            model,
            "OCR7SJ8Mod2");

        var root = Assert.IsType<XElement>(document.Root);
        var ns = root.Name.Namespace;
        var ied = Assert.Single(root.Elements(ns + "IED"));
        Assert.Equal("OCR7SJ8Mod2", (string?)ied.Attribute("name"));
        Assert.Equal("OCR7SJ8Mod2", (string?)root.Descendants(ns + "ConnectedAP").Single().Attribute("iedName"));

        var logicalDevices = root.Descendants(ns + "LDevice").ToArray();
        Assert.Equal(2, logicalDevices.Length);
        Assert.Contains(logicalDevices, ld =>
            (string?)ld.Attribute("inst") == "OCR7SJ8Application" &&
            (string?)ld.Attribute("ldName") == "OCR7SJ8Application");
        Assert.Contains(logicalDevices, ld =>
            (string?)ld.Attribute("inst") == "OCR7SJ8CB1" &&
            (string?)ld.Attribute("ldName") == "OCR7SJ8CB1");

        var report = Assert.Single(root.Descendants(ns + "ReportControl"));
        Assert.Equal("urcbD0101", (string?)report.Attribute("name"));
        var fcda = Assert.Single(root.Descendants(ns + "FCDA"));
        Assert.Equal("OCR7SJ8CB1", (string?)fcda.Attribute("ldInst"));
        Assert.DoesNotContain("OCR7SJ8Mod2OCR7SJ8Application", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Exporter_Keeps_Implicit_Product_Naming_When_Domain_Starts_With_Ied_Name()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            IedName = "IED1",
            LogicalDevices = [LogicalDevice("IED1PROT")]
        };

        var generated = LiveIedSclExporter.BuildDocument(model, new LiveIedSclExportOptions());
        var document = AuthoritativeLiveIedSclExporter.ApplyIdentity(generated, model, "IED1");
        var root = Assert.IsType<XElement>(document.Root);
        var ns = root.Name.Namespace;
        var logicalDevice = Assert.Single(root.Descendants(ns + "LDevice"));

        Assert.Equal("PROT", (string?)logicalDevice.Attribute("inst"));
        Assert.Null(logicalDevice.Attribute("ldName"));
    }

    private static LiveIedLogicalDeviceModel LogicalDevice(string domain)
        => new()
        {
            MmsDomain = domain,
            Inst = domain,
            LogicalNodes =
            [
                new LiveIedLogicalNodeModel
                {
                    Name = "LLN0",
                    LnClass = "LLN0",
                    ProposedLnTypeId = $"LN_{domain}_LLN0"
                },
                new LiveIedLogicalNodeModel
                {
                    Name = "XCBR1",
                    LnClass = "XCBR",
                    LnInst = "1",
                    ProposedLnTypeId = $"LN_{domain}_XCBR1"
                }
            ]
        };
}
