using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalLiveIedSclExporterTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Theory]
    [InlineData(SclSchemaProfile.Edition2V31)]
    [InlineData(SclSchemaProfile.Edition1V16)]
    public void WriteFiles_RoundTripsCanonicalAssociationWithoutExporterDefaults(SclSchemaProfile schema)
    {
        var root = Path.Combine(Path.GetTempPath(), "ariec-canonical-scl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, schema == SclSchemaProfile.Edition2V31 ? "ied.iid" : "ied.icd");
        try
        {
            var canonical = CreateCanonical();
            CanonicalLiveIedSclExporter.WriteFiles(canonical, path, schema);

            var document = XDocument.Load(path);
            var address = document.Descendants(Scl + "ConnectedAP").Single().Element(Scl + "Address")!;
            var values = address.Elements(Scl + "P")
                .ToDictionary(e => (string)e.Attribute("type")!, e => e.Value, StringComparer.OrdinalIgnoreCase);

            Assert.Equal("10.20.30.40", values["IP"]);
            Assert.Equal("1,1,1,999,1", values["OSI-AP-Title"]);
            Assert.Equal("12", values["OSI-AE-Qualifier"]);
            Assert.Equal("00000001", values["OSI-PSEL"]);
            Assert.Equal("0001", values["OSI-SSEL"]);
            Assert.Equal("0001", values["OSI-TSEL"]);
            Assert.False(values.ContainsKey("IP-SUBNET"));
            Assert.False(values.ContainsKey("IP-GATEWAY"));

            var profiles = SclMmsAssociationProfileReader.Read(document);
            var remote = Assert.Single(profiles.AccessPoints);
            var plan = SclAssistedMmsAssociationPlanBuilder.BuildExact(
                remote,
                MmsLocalAssociationProfile.SclInteroperabilityDefault);
            Assert.True(plan.IsSuccess, string.Join(" | ", plan.Errors));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateCanonicalCommunication_RejectsMissingRemoteApTitle()
    {
        var canonical = CreateCanonical();
        canonical = new LiveIedCanonicalModel
        {
            Discovery = canonical.Discovery,
            Communication = new LiveIedCommunicationEvidence
            {
                Host = canonical.Communication.Host,
                AccessPointName = canonical.Communication.AccessPointName,
                Association = new SclIsoAssociationAddress
                {
                    AeQualifier = 12,
                    AeQualifierText = "12",
                    PresentationSelector = "00000001",
                    SessionSelector = "0001",
                    TransportSelector = "0001"
                }
            }
        };

        var ex = Assert.Throws<InvalidDataException>(() =>
            CanonicalLiveIedSclExporter.ValidateCanonicalCommunication(canonical));
        Assert.Contains("OSI-AP-Title", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteFiles_PreservesPhysicalReportCapacityAfterLogicalProjection()
    {
        var root = Path.Combine(Path.GetTempPath(), "ariec-canonical-rcb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "ied.iid");
        try
        {
            var canonical = CreateCanonical(reportControlCount: 3);
            CanonicalLiveIedSclExporter.WriteFiles(canonical, path);
            var document = XDocument.Load(path);
            var conf = document.Descendants(Scl + "ConfReportControl").Single();
            Assert.Equal("3", (string?)conf.Attribute("max"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static LiveIedCanonicalModel CreateCanonical(int reportControlCount = 0)
    {
        var reports = Enumerable.Range(1, reportControlCount)
            .Select(i => new LiveIedReportControlModel
            {
                Name = $"R{i:00}",
                Reference = $"IEDLD0/LLN0$RP$R{i:00}",
                Domain = "IEDLD0",
                LogicalNode = "LLN0",
                Buffered = false
            })
            .ToArray();

        return new LiveIedCanonicalModel
        {
            Discovery = new LiveIedModelDiscoveryDocument
            {
                Host = "10.20.30.40",
                IedName = "IED",
                AccessPointName = "AP1",
                ReportControls = reports
            },
            Communication = new LiveIedCommunicationEvidence
            {
                Source = "AcceptedAssociationProfile",
                AssociationProfileName = "BalancedApTitle",
                Host = "10.20.30.40",
                Port = 102,
                AccessPointName = "AP1",
                Association = new SclIsoAssociationAddress
                {
                    ApTitle = "1,1,1,999,1",
                    AeQualifierText = "12",
                    AeQualifier = 12,
                    PresentationSelector = "00000001",
                    SessionSelector = "0001",
                    TransportSelector = "0001"
                }
            }
        };
    }
}
